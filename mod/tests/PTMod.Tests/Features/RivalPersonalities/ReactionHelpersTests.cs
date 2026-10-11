using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Ids;
using PTMod.Features.RivalPersonalities;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The small pure parts of the reactions (TSK-013..015): the names the reactions are known by (<see cref="ReactionTags"/>), the arguments of the events and
    /// the saved lines of the effects (<see cref="EffectArgs"/>), and the numbers a reaction takes from the archetype and <c>config.json</c>
    /// (<see cref="ReactionParams"/>).
    /// </summary>
    [Covers(typeof(ReactionTags), typeof(EffectArgs), typeof(ReactionParams))]
    public class ReactionHelpersTests
    {
        // ---- ReactionTags ----

        [Test]
        public void TheTags_AreTheOnesOfTheDesign_AndPassTheIdRulesOfTheCore()
        {
            Assert.Equal("rival:intel:r-17", ReactionTags.Intel("r-17"));
            Assert.Equal("rival:counter:r-17", ReactionTags.Counter("r-17"));
            Assert.Equal("openIntel", ReactionTags.ActionOpenIntel);

            var problems = new List<string>();
            try
            {
                ModIds.Load(RivalTestKit.CorePrefixes(), problems);
                Assert.Equal(0, problems.Count, string.Join("; ", problems));
                Assert.True(ModIds.IsTagAllowed(RivalTestKit.Feature, ReactionTags.Intel("r-17")), ModIds.Explain(IdKind.Tag, RivalTestKit.Feature, ReactionTags.Intel("r-17")));
                Assert.True(ModIds.IsTagAllowed(RivalTestKit.Feature, ReactionTags.Counter("r-17")));
                foreach (ReactionType type in new[] { ReactionType.PriceCut, ReactionType.CounterLaunch, ReactionType.OemPush, ReactionType.Poach, ReactionType.Smear })
                {
                    string eventId = ReactionTags.EventIdOf(type);
                    Assert.True(ModIds.IsEventIdAllowed(RivalTestKit.Feature, eventId), ModIds.Explain(IdKind.EventId, RivalTestKit.Feature, eventId));
                    Assert.True(eventId.StartsWith(ReactionTags.EventPrefix, StringComparison.Ordinal));
                }
            }
            finally
            {
                ModIds.Clear();
            }
        }

        [Test]
        public void TheTags_ParseBack_AndOnlyTheirOwnKind()
        {
            string id;
            Assert.True(ReactionTags.TryParseIntel("rival:intel:r-17", out id));
            Assert.Equal("r-17", id);
            Assert.True(ReactionTags.TryParseCounter("rival:counter:r-3", out id));
            Assert.Equal("r-3", id);

            Assert.False(ReactionTags.TryParseIntel("rival:counter:r-17", out id));
            Assert.False(ReactionTags.TryParseCounter("rival:intel:r-17", out id));
            Assert.False(ReactionTags.TryParseIntel("rival:intel:", out id), "an empty id is no id");
            Assert.False(ReactionTags.TryParseIntel("onb:welcome", out id));
            Assert.False(ReactionTags.TryParseIntel(null, out id));
            Assert.Null(id);
        }

        [Test]
        public void TheEvents_AreOneForEachReaction_AndMapBack()
        {
            Assert.Equal("rival.priceCut", ReactionTags.EventIdOf(ReactionType.PriceCut));
            Assert.Equal("rival.counterLaunch", ReactionTags.EventIdOf(ReactionType.CounterLaunch));
            Assert.Equal("rival.oemPush", ReactionTags.EventIdOf(ReactionType.OemPush));
            Assert.Equal("rival.poach", ReactionTags.EventIdOf(ReactionType.Poach));
            Assert.Equal("rival.smear", ReactionTags.EventIdOf(ReactionType.Smear));
            Assert.Null(ReactionTags.EventIdOf(ReactionType.Partnership), "the friendly offer is no event");

            for (int i = 0; i < 5; i++)
            {
                ReactionType back;
                Assert.True(ReactionTags.TryReactionOfEvent(ReactionTags.EventIdOf((ReactionType)i), out back));
                Assert.Equal((ReactionType)i, back);
            }
            ReactionType none;
            Assert.False(ReactionTags.TryReactionOfEvent("es.patent_troll", out none));
            Assert.False(ReactionTags.TryReactionOfEvent(null, out none));
        }

        [Test]
        public void WhoIsTheTarget_AndWhoStartsTheReaction()
        {
            Assert.True(ReactionTags.TargetsPlayer(ReactionType.Poach));
            Assert.True(ReactionTags.TargetsPlayer(ReactionType.Smear));
            Assert.False(ReactionTags.TargetsPlayer(ReactionType.PriceCut));
            Assert.False(ReactionTags.TargetsPlayer(ReactionType.CounterLaunch));
            Assert.False(ReactionTags.TargetsPlayer(ReactionType.OemPush));

            // PriceCut, CounterLaunch and Poach have an effect of the feature; OemPush and Smear are standard effects of the event system
            Assert.True(ReactionTags.StartsInOwnHandler(ReactionType.PriceCut));
            Assert.True(ReactionTags.StartsInOwnHandler(ReactionType.CounterLaunch));
            Assert.True(ReactionTags.StartsInOwnHandler(ReactionType.Poach));
            Assert.False(ReactionTags.StartsInOwnHandler(ReactionType.OemPush));
            Assert.False(ReactionTags.StartsInOwnHandler(ReactionType.Smear));

            // prices and boosts are held by the feature; the rest is the event system's
            Assert.True(ReactionTags.HoldsState(ReactionType.PriceCut));
            Assert.True(ReactionTags.HoldsState(ReactionType.CounterLaunch));
            Assert.False(ReactionTags.HoldsState(ReactionType.Poach));
        }

        // ---- EffectArgs ----

        [Test]
        public void TheArgumentsOfTheEvent_GoThere_AndBack()
        {
            string[] args = EffectArgs.ForFire("AMD", 1, "r-17", "ai:1");
            Assert.Equal(new[] { "AMD", "1", "r-17", "ai:1" }, args);

            EffectArgs read = EffectArgs.FromFire(args);
            Assert.Equal("AMD", read.RivalName);
            Assert.Equal(1, read.Segment);
            Assert.Equal("r-17", read.ReactionId);
            Assert.Equal("ai:1", read.RivalKey);
            Assert.Equal(1, read.UniqueId);
            Assert.True(read.IsComplete);
        }

        [Test]
        public void ThePoachingGivesThePriceInThePlaceOfTheSegment()
        {
            string[] args = EffectArgs.ForFire("IBM", "$250K", "r-21", "ai:4");
            EffectArgs read = EffectArgs.FromFire(args);

            Assert.Equal("$250K", args[1], "{1} of the text of the poaching");
            Assert.Equal(-1, read.Segment, "the price is no segment");
            Assert.Equal(4, read.UniqueId);
            Assert.True(read.IsComplete);
        }

        [Test]
        public void DamagedArguments_GiveAnIncompleteObject_NotAnException()
        {
            Assert.False(EffectArgs.FromFire(null).IsComplete);
            Assert.False(EffectArgs.FromFire(new string[0]).IsComplete);
            Assert.False(EffectArgs.FromFire(new[] { "AMD" }).IsComplete);
            Assert.False(EffectArgs.FromFire(new[] { "AMD", "1", "r-1", "player" }).IsComplete, "the player is no rival");
            Assert.False(EffectArgs.FromFire(new[] { "AMD", "1", "", "ai:1" }).IsComplete, "a reaction needs an id");
            Assert.Equal(-1, EffectArgs.FromFire(new[] { "AMD", "9", "r-1", "ai:1" }).Segment, "a segment is 0..3");
            Assert.Equal(-1, EffectArgs.FromFire(new[] { "AMD", "x", "r-1", "ai:1" }).Segment);
            Assert.Equal(-1, EffectArgs.FromFire(new string[] { null, null, null, null }).UniqueId);
            Assert.Equal("", EffectArgs.FromFire(new string[] { null, null, null, null }).RivalName);
        }

        [Test]
        public void TheSavedLine_KeepsTheFourFixedMembers_AndTheExtrasAfterThem()
        {
            EffectArgs original = EffectArgs.FromFire(EffectArgs.ForFire("AMD", 2, "r-5", "ai:1"));
            string[] saved = original.ToSaved("15", "Low End");

            Assert.Equal(new[] { "r-5", "2", "ai:1", "AMD", "15", "Low End" }, saved);
            EffectArgs back = EffectArgs.FromSaved(saved);
            Assert.Equal("r-5", back.ReactionId);
            Assert.Equal(2, back.Segment);
            Assert.Equal("ai:1", back.RivalKey);
            Assert.Equal("AMD", back.RivalName);
            Assert.True(back.IsComplete);
            Assert.Equal("15", EffectArgs.ExtraAt(saved, 0));
            Assert.Equal("Low End", EffectArgs.ExtraAt(saved, 1));
            Assert.Equal("", EffectArgs.ExtraAt(saved, 2), "past the end");
            Assert.Equal("", EffectArgs.ExtraAt(null, 0));

            // what Describe hands back to the effect is read again by the next call: the line is stable
            Assert.Equal(saved, EffectArgs.FromSaved(saved).ToSaved("15", "Low End"));
            Assert.Equal(4, original.ToSaved().Length);
            Assert.False(EffectArgs.FromSaved(null).IsComplete);
            Assert.False(EffectArgs.FromSaved(new[] { "r-5" }).IsComplete);
        }

        [Test]
        public void TheNumbers_AreWrittenTheInvariantWay()
        {
            Assert.Equal("15", EffectArgs.Percent(0.15f));
            Assert.Equal("8", EffectArgs.Percent(0.08f));
            Assert.Equal("8.5", EffectArgs.Percent(0.085));
            Assert.Equal("0", EffectArgs.Percent(double.NaN));
            Assert.Equal("1.35", EffectArgs.Multiplier(1.35f));
            Assert.Equal("1.1", EffectArgs.Multiplier(1.10f));
            Assert.Equal("1", EffectArgs.Multiplier(double.NaN));
            Assert.Equal("-8", EffectArgs.Number(-8.0));
        }

        // ---- ReactionParams ----

        private static Archetype Of(string id)
        {
            Archetype archetype;
            Assert.True(RivalTestKit.LoadShippedCatalog().TryGet(id, out archetype), id);
            return archetype;
        }

        [Test]
        public void TheArchetype_MayChangeTheDepthOfThePriceCut()
        {
            RivalConfig config = RivalTestKit.LoadShippedCatalog().Config;

            Assert.Near(0.15, ReactionParams.PriceCutDepth(Of("price_warrior"), config), 1e-6);
            Assert.Near(0.10, ReactionParams.PriceCutDepth(Of("niche_specialist"), config), 1e-6);
            Assert.Near(0.08, ReactionParams.PriceCutDepth(Of("hegemon"), config), 1e-6);
            Assert.Near(0.08, ReactionParams.PriceCutFloorMargin(Of("price_warrior"), config), 1e-6, "the floor is the default of config.json");
        }

        [Test]
        public void TheDefaults_AreTheNumbersOfTheDesign()
        {
            RivalConfig config = RivalConfig.Defaults();

            Assert.Near(0.08, ReactionParams.PriceCutDepth(null, config), 1e-6);
            Assert.Near(0.08, ReactionParams.PriceCutFloorMargin(null, null), 1e-6);
            Assert.Near(1.35, ReactionParams.ApprovalBoost(null, config), 1e-6);
            Assert.Near(1.25, ReactionParams.FundingBoost(null, config), 1e-6);
            Assert.Near(1.10, ReactionParams.PoachRivalResearchSpeed(null, config), 1e-6);
            Assert.Equal(120, ReactionParams.PoachRivalBoostDays(null, config));

            config.reactions.PriceCut.depth = 0.2f;
            Assert.Near(0.2, ReactionParams.PriceCutDepth(null, config), 1e-6, "config.json is the default of every archetype");
        }

        [Test]
        public void ADamagedNumber_IsHeldToItsRange()
        {
            var archetype = new Archetype { id = "test" };
            archetype.reactionParams["PriceCut"] = new Dictionary<string, float> { { "depth", 0.9f }, { "floorMargin", float.NaN } };
            archetype.reactionParams["CounterLaunch"] = new Dictionary<string, float> { { "approvalBoost", 0.2f }, { "fundingBoost", 9f } };
            archetype.reactionParams["Poach"] = new Dictionary<string, float> { { "rivalResearchSpeed", 5f }, { "rivalBoostDays", 5000f } };
            RivalConfig config = RivalConfig.Defaults();

            Assert.Near(0.5, ReactionParams.PriceCutDepth(archetype, config), 1e-6);
            Assert.Near(0.08, ReactionParams.PriceCutFloorMargin(archetype, config), 1e-6, "NaN gives the design number");
            Assert.Near(1.0, ReactionParams.ApprovalBoost(archetype, config), 1e-6);
            Assert.Near(3.0, ReactionParams.FundingBoost(archetype, config), 1e-6);
            Assert.Near(2.0, ReactionParams.PoachRivalResearchSpeed(archetype, config), 1e-6);
            Assert.Equal(720, ReactionParams.PoachRivalBoostDays(archetype, config));
        }
    }
}
