using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>The size of the effects of the feature (FR-10 item 3, design.md formula 2): the same arithmetic the event system applies to the effects it resolves.</summary>
    public static class IntensityMath
    {
        /// <summary>
        /// <c>m' = 1 + (m − 1) × intensity</c> for an effect marked <c>scalable</c>; <paramref name="multiplier"/> itself otherwise. The result is never
        /// below 0 (a channel multiplier is 0 or more). A multiplier or an intensity that is not a finite number is returned unchanged.
        /// </summary>
        public static float Scale(float multiplier, float intensity, bool scalable)
        {
            if (!scalable || float.IsNaN(multiplier) || float.IsInfinity(multiplier) || float.IsNaN(intensity) || float.IsInfinity(intensity)) return multiplier;

            double scaled = 1.0 + (multiplier - 1.0) * intensity;
            return scaled < 0.0 ? 0f : (float)scaled;
        }
    }

    /// <summary>The names of the standard effects of the event system that the catch-up understands.</summary>
    public static class CatchUpEffectTypes
    {
        public const string MarketDemand = "MarketDemand";
        public const string ModifierTimed = "ModifierTimed";
        public const string FactoryOutput = "FactoryOutput";

        /// <summary>The channel of <c>FactoryOutput</c> (<c>ModChannels.ProductionOutput</c>).</summary>
        public const string FactoryOutputChannel = "production.output";

        public static bool IsCaughtUp(string type)
        {
            return type == MarketDemand || type == ModifierTimed || type == FactoryOutput;
        }
    }

    /// <summary>One standard timed effect of an event of the pack, as the catch-up reads it from the definition.</summary>
    public sealed class CatchUpEffect
    {
        /// <summary>One of <see cref="CatchUpEffectTypes"/>.</summary>
        public string Type { get; set; }

        /// <summary><c>MarketDemand</c>: the segment name (<c>highend</c>, <c>midrange</c>, <c>lowend</c>, <c>industries</c>, <c>all</c>); null or <c>all</c> is every segment.</summary>
        public string Segment { get; set; }

        /// <summary><c>ModifierTimed</c>: the channel.</summary>
        public string Channel { get; set; }

        /// <summary><c>ModifierTimed</c>: the effect acts on the whole market (<c>scope: Global</c>), not on the target company.</summary>
        public bool GlobalScope { get; set; }

        public float Multiplier { get; set; } = 1f;

        /// <summary>The deviation is scaled by the intensity (<see cref="IntensityMath"/>).</summary>
        public bool Scalable { get; set; }

        /// <summary>How long the effect lasts from the moment its stage fires, in days; 0 or less - not a timed effect, nothing to catch up.</summary>
        public int DurationDays { get; set; }
    }

    /// <summary>What the catch-up needs to know about the world; the game gives the real one (<c>CatchUpService</c>), the tests a fake.</summary>
    public interface ICatchUpWorld
    {
        /// <summary>The switch of the category.</summary>
        bool IsCategoryEnabled(string category);

        /// <summary>The standard timed effects (<c>MarketDemand</c>, <c>ModifierTimed</c>, <c>FactoryOutput</c>) of the event, or null when the pack has no such event.</summary>
        IReadOnlyList<CatchUpEffect> EffectsOf(string eventId);

        /// <summary>The companies for the roles of the entry now.</summary>
        RoleResult ResolveRoles(CalendarEntry entry);

        /// <summary>The conditions of the entry hold now (<c>featureActive</c> and the like).</summary>
        bool ConditionsHold(CalendarEntry entry, RoleResult roles);

        /// <summary>The <c>onlyIf</c> of a fire holds for the company.</summary>
        bool OnlyIfHolds(FireSpec fire, RoleResult roles, string companyKey);

        /// <summary>The intensity setting (0.5 to 1.5).</summary>
        float Intensity { get; }
    }

    /// <summary>One entry of <c>ModModifiers</c> that the catch-up asks for.</summary>
    public sealed class CatchUpItem
    {
        public string EntryId { get; set; }
        public int StageIndex { get; set; }
        public string EventId { get; set; }

        /// <summary>The channel; null for a demand effect (then <see cref="DemandSegment"/> names the segment).</summary>
        public string Channel { get; set; }

        /// <summary>The segment of a <c>MarketDemand</c> effect (its channel is <c>market.demand.&lt;segment&gt;</c>); <see cref="Segment.Any"/> for the others.</summary>
        public Segment DemandSegment { get; set; } = Segment.Any;

        /// <summary>The effect acts on the whole market; otherwise on <see cref="CompanyKey"/>.</summary>
        public bool Global { get; set; }

        public string CompanyKey { get; set; }

        /// <summary>The multiplier with the intensity applied.</summary>
        public float Multiplier { get; set; }

        /// <summary>The days the effect still has at the start of the game.</summary>
        public int RemainingDays { get; set; }
    }

    /// <summary>The result of <see cref="CatchUpPlanner.Plan"/>: the entries to ask <c>ModModifiers.Add</c> for and the entries they came from.</summary>
    public sealed class CatchUpPlan
    {
        public List<CatchUpItem> Items { get; } = new List<CatchUpItem>();

        /// <summary>The ids of the entries that contribute at least one item, in the order of the calendar.</summary>
        public List<string> EntryIds { get; } = new List<string>();

        /// <summary>The longest remaining time among the items of an entry (what the letter says), 0 for an entry that has none.</summary>
        public int LongestRemaining(string entryId)
        {
            int longest = 0;
            foreach (CatchUpItem item in Items)
            {
                if (item.EntryId == entryId && item.RemainingDays > longest) longest = item.RemainingDays;
            }
            return longest;
        }
    }

    /// <summary>
    /// What a game that starts late still feels of the past (FR-4 items 2-3; design.md, Component 9): the standard timed effects of the stages that
    /// are still running at the start day, turned into entries for <c>ModModifiers.Add</c> with the days they have left.
    /// <list type="bullet">
    /// <item><b>Which entries.</b> Those with <c>catchUp</c>, an anchor before the start, a category that is on, roles that are found now and conditions that
    /// hold now (a crypto boom needs <c>gpu-product-line</c>). The jitter is not counted: the anchor is the date.</item>
    /// <item><b>Which stages.</b> Those that began by the start day (<c>anchor + offset ≤ start</c>; a stage that begins later would belong to the future
    /// of an entry that is not going to fire) and have not ended (<c>anchor + offset + duration &gt; start</c>); the days left are the difference.</item>
    /// <item><b>Which effects.</b> <c>MarketDemand</c>, <c>ModifierTimed</c> and <c>FactoryOutput</c> of the events of the stage's fires, not the choices,
    /// not the effects of the feature's own types (cases, rules, wars): those are not caught up.</item>
    /// <item><b>For whom.</b> A whole-market effect once (when the fire reaches anyone), an effect on the target once per company of the fire's role
    /// that passes the fire's <c>onlyIf</c>; <c>global</c> and <c>player</c> fires reach the player.</item>
    /// </list>
    /// </summary>
    public static class CatchUpPlanner
    {
        public static CatchUpPlan Plan(int startDay, IReadOnlyList<CalendarEntry> entries, ICatchUpWorld world)
        {
            var plan = new CatchUpPlan();
            if (entries == null || world == null) return plan;

            foreach (CalendarEntry entry in entries)
            {
                if (entry == null || !entry.CatchUp || entry.Stages == null || !entry.Anchor.IsSet) continue;
                if (entry.Anchor.Days >= startDay) continue;
                if (!world.IsCategoryEnabled(entry.Category)) continue;

                RoleResult roles = world.ResolveRoles(entry);
                if (roles == null || !roles.Ok || !world.ConditionsHold(entry, roles)) continue;

                int before = plan.Items.Count;
                for (int k = 0; k < entry.Stages.Count; k++) PlanStage(entry, k, startDay, roles, world, plan);
                if (plan.Items.Count > before) plan.EntryIds.Add(entry.Id);
            }
            return plan;
        }

        private static void PlanStage(CalendarEntry entry, int stageIndex, int startDay, RoleResult roles, ICatchUpWorld world, CatchUpPlan plan)
        {
            StageSpec stage = entry.Stages[stageIndex];
            if (stage == null || stage.Fires == null) return;

            int begins = entry.Anchor.Days + stage.OffsetDays;
            if (begins > startDay) return;

            foreach (FireSpec fire in stage.Fires)
            {
                if (fire == null || string.IsNullOrEmpty(fire.EventId)) continue;

                IReadOnlyList<CatchUpEffect> effects = world.EffectsOf(fire.EventId);
                if (effects == null || effects.Count == 0) continue;

                List<string> companies = CompaniesReached(fire, roles, world);
                if (companies.Count == 0) continue;

                foreach (CatchUpEffect effect in effects)
                {
                    if (effect == null || !CatchUpEffectTypes.IsCaughtUp(effect.Type) || effect.DurationDays <= 0) continue;

                    int remaining = begins + effect.DurationDays - startDay;
                    if (remaining <= 0) continue;

                    float multiplier = IntensityMath.Scale(effect.Multiplier, world.Intensity, effect.Scalable);
                    Add(entry, stageIndex, fire, effect, multiplier, remaining, companies, plan);
                }
            }
        }

        private static void Add(CalendarEntry entry, int stageIndex, FireSpec fire, CatchUpEffect effect, float multiplier, int remaining, List<string> companies, CatchUpPlan plan)
        {
            switch (effect.Type)
            {
                case CatchUpEffectTypes.MarketDemand:
                    foreach (Segment segment in DemandSegments(effect.Segment))
                    {
                        plan.Items.Add(Item(entry, stageIndex, fire, multiplier, remaining, true, null, segment, null));
                    }
                    break;

                case CatchUpEffectTypes.ModifierTimed:
                    if (string.IsNullOrEmpty(effect.Channel)) return;
                    if (effect.GlobalScope)
                    {
                        plan.Items.Add(Item(entry, stageIndex, fire, multiplier, remaining, true, effect.Channel, Segment.Any, null));
                    }
                    else
                    {
                        foreach (string key in companies) plan.Items.Add(Item(entry, stageIndex, fire, multiplier, remaining, false, effect.Channel, Segment.Any, key));
                    }
                    break;

                case CatchUpEffectTypes.FactoryOutput:
                    foreach (string key in companies)
                    {
                        plan.Items.Add(Item(entry, stageIndex, fire, multiplier, remaining, false, CatchUpEffectTypes.FactoryOutputChannel, Segment.Any, key));
                    }
                    break;
            }
        }

        private static CatchUpItem Item(CalendarEntry entry, int stageIndex, FireSpec fire, float multiplier, int remaining, bool global, string channel,
            Segment demandSegment, string companyKey)
        {
            return new CatchUpItem
            {
                EntryId = entry.Id,
                StageIndex = stageIndex,
                EventId = fire.EventId,
                Channel = channel,
                DemandSegment = demandSegment,
                Global = global,
                CompanyKey = companyKey,
                Multiplier = multiplier,
                RemainingDays = remaining,
            };
        }

        /// <summary>The segments a demand effect touches: its own, or the four when it says <c>all</c> (or nothing).</summary>
        public static IReadOnlyList<Segment> DemandSegments(string name)
        {
            Segment one;
            if (string.IsNullOrEmpty(name) || string.Equals(name, "all", StringComparison.OrdinalIgnoreCase) || !HistSegments.TryParse(name, out one) || one == Segment.Any)
                return new[] { Segment.HighEnd, Segment.MidRange, Segment.LowEnd, Segment.Industries };
            return new[] { one };
        }

        /// <summary>The companies a fire reaches at the start: the ones of its role that pass its <c>onlyIf</c>.</summary>
        private static List<string> CompaniesReached(FireSpec fire, RoleResult roles, ICatchUpWorld world)
        {
            var reached = new List<string>();
            foreach (string key in DispatcherCore.CompaniesOf(fire.Role, roles))
            {
                if (fire.OnlyIf == null || world.OnlyIfHolds(fire, roles, key)) reached.Add(key);
            }
            return reached;
        }
    }
}
