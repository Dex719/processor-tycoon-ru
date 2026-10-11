using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Features.HistoricalEvents;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The catch-up of a late start (TSK-007; FR-4, AC-4.1 to AC-4.3, AC-10.1 for the intensity): which stages are still running at the start, with how many days
    /// left, on which channels and for whom, and what is left out. The world is a fake; the writing into <c>ModModifiers</c> and the letter are the
    /// game side (<c>CatchUpService</c>), checked here on its source.
    /// </summary>
    [Covers(typeof(CatchUpPlanner), typeof(CatchUpPlan), typeof(CatchUpItem), typeof(CatchUpEffect), typeof(CatchUpEffectTypes), typeof(ICatchUpWorld), typeof(IntensityMath))]
    public class CatchUpServiceTests
    {
        // ---- the calendar of the scenarios (the entries of the design table, reduced to what the catch-up reads) ----

        private static CalendarEntry Entry(string id, string anchor, string category, bool catchUp, string stages)
        {
            SimpleDate a;
            Assert.True(SimpleDate.TryParse(anchor, out a), anchor);
            return HistFixture.Entry(
                "{ 'id': '" + id + "', 'category': '" + category + "', 'catchUp': " + (catchUp ? "true" : "false") + ", 'anchor': '" + anchor + "'," +
                "  'window': { 'from': '" + anchor + "', 'to': '" + a.AddDays(365).ToString() + "' }, 'roles': [], 'conditions': []," +
                "  'stages': " + stages + ", 'fallback': 'skip' }");
        }

        private static string Fire(string eventId, string role = "global", string onlyIf = null)
        {
            return "{ 'eventId': '" + eventId + "', 'role': '" + role + "'" + (onlyIf == null ? string.Empty : ", 'onlyIf': " + onlyIf) + " }";
        }

        private static string Stage(int offset, params string[] fires)
        {
            return "{ 'offsetDays': " + offset + ", 'fires': [ " + string.Join(", ", fires) + " ] }";
        }

        private static CalendarEntry Shortage()
        {
            return Entry("hist.2020.shortage", "2020-12-01", "shock", true, "[ " + Stage(0, Fire("hist.2020.shortage"), Fire("hist.2020.shortage.output", "allFabless")) + " ]");
        }

        private static CalendarEntry Covid()
        {
            return Entry("hist.2020.covid", "2020-03-11", "disaster", true,
                "[ " + Stage(0, Fire("hist.2020.covid.slump")) + ", " + Stage(90, Fire("hist.2020.covid.rebound")) + " ]");
        }

        private static CalendarEntry Crypto2021()
        {
            return Entry("hist.2021.cryptoboom", "2021-04-01", "boom", true, "[ " + Stage(0, Fire("hist.2021.cryptoboom")) + " ]");
        }

        private static CalendarEntry Crypto2017()
        {
            return Entry("hist.2017.cryptoboom", "2017-10-01", "boom", true, "[ " + Stage(0, Fire("hist.2017.cryptoboom")) + " ]");
        }

        private sealed class World : ICatchUpWorld
        {
            public readonly HashSet<string> Enabled = new HashSet<string>(HistCategories.All.Where(HistCategories.DefaultEnabled), StringComparer.Ordinal);
            public readonly Dictionary<string, List<CatchUpEffect>> Effects = new Dictionary<string, List<CatchUpEffect>>(StringComparer.Ordinal);
            public readonly Dictionary<string, RoleResult> RolesOf = new Dictionary<string, RoleResult>(StringComparer.Ordinal);
            public readonly HashSet<string> FalseConditions = new HashSet<string>(StringComparer.Ordinal);
            public readonly List<string> OnlyIfAsked = new List<string>();
            public Func<FireSpec, string, bool> OnlyIf = (fire, key) => true;
            public float Intensity { get; set; } = 1f;

            public World()
            {
                // The events of the pack that the scenarios fire.
                Add("hist.2020.shortage", Timed("production.unitCost", 1.15f, 730, true), Timed("contract.reward", 1.15f, 730, true));
                Add("hist.2020.shortage.output", new CatchUpEffect { Type = CatchUpEffectTypes.FactoryOutput, Multiplier = 0.90f, Scalable = true, DurationDays = 540 });
                Add("hist.2020.covid.slump", Demand("all", 0.85f, 90));
                Add("hist.2020.covid.rebound", Demand("midrange", 1.30f, 540), Demand("lowend", 1.30f, 540));
                Add("hist.2021.cryptoboom", Timed("gpu.demand", 1.45f, 365, true));
                Add("hist.2017.cryptoboom", Timed("gpu.demand", 1.35f, 365, true));
            }

            public static CatchUpEffect Timed(string channel, float multiplier, int days, bool global, bool scalable = true)
            {
                return new CatchUpEffect { Type = CatchUpEffectTypes.ModifierTimed, Channel = channel, Multiplier = multiplier, DurationDays = days, GlobalScope = global, Scalable = scalable };
            }

            public static CatchUpEffect Demand(string segment, float multiplier, int days, bool scalable = true)
            {
                return new CatchUpEffect { Type = CatchUpEffectTypes.MarketDemand, Segment = segment, Multiplier = multiplier, DurationDays = days, Scalable = scalable };
            }

            public void Add(string eventId, params CatchUpEffect[] effects)
            {
                Effects[eventId] = effects.ToList();
            }

            public bool IsCategoryEnabled(string category)
            {
                return Enabled.Contains(category);
            }

            public IReadOnlyList<CatchUpEffect> EffectsOf(string eventId)
            {
                List<CatchUpEffect> list;
                return Effects.TryGetValue(eventId, out list) ? list : null;
            }

            public RoleResult ResolveRoles(CalendarEntry entry)
            {
                RoleResult roles;
                if (RolesOf.TryGetValue(entry.Id, out roles)) return roles;

                // Without a stated result the built-in groups are the ones of a small world.
                var result = new RoleResult();
                result.Targets[RoleKinds.AllFabless] = new List<string> { "ai:10" };
                result.Targets[RoleKinds.AllManufacturers] = new List<string> { "player", "ai:0" };
                result.Targets[RoleKinds.AllCpuCompanies] = new List<string> { "player", "ai:0", "ai:10" };
                return result;
            }

            public bool ConditionsHold(CalendarEntry entry, RoleResult roles)
            {
                return !FalseConditions.Contains(entry.Id);
            }

            public bool OnlyIfHolds(FireSpec fire, RoleResult roles, string companyKey)
            {
                OnlyIfAsked.Add(fire.EventId + "@" + companyKey);
                return OnlyIf(fire, companyKey);
            }
        }

        private static CatchUpPlan Plan(World world, string start, params CalendarEntry[] entries)
        {
            SimpleDate date;
            Assert.True(SimpleDate.TryParse(start, out date), start);
            return CatchUpPlanner.Plan(date.Days, entries, world);
        }

        private static IEnumerable<CatchUpItem> Of(CatchUpPlan plan, string entryId)
        {
            return plan.Items.Where(i => i.EntryId == entryId);
        }

        // ---- the acceptance scenarios ----

        [Test]
        public void AC_4_1_StartIn2021_WithDisasterOn_CatchesUpTheShortageAndTheRebound()
        {
            var world = new World();
            world.Enabled.Add("disaster");

            CatchUpPlan plan = Plan(world, "2021-01-01", Shortage(), Covid());

            // hist.2020.shortage: anchor 2020-12-01, 730 days, started 31 days ago: 699 left.
            CatchUpItem unitCost = Of(plan, "hist.2020.shortage").Single(i => i.Channel == "production.unitCost");
            Assert.Equal(699, unitCost.RemainingDays);
            Assert.True(unitCost.Global);
            Assert.Near(1.15, unitCost.Multiplier, 1e-6);
            Assert.Equal(699, Of(plan, "hist.2020.shortage").Single(i => i.Channel == "contract.reward").RemainingDays);

            // hist.2020.covid: the first stage (the slump, 90 days from 2020-03-11) ended on 2020-06-09; the second (the rebound from 2020-06-09, 540 days) has 334 left.
            Assert.Equal(0, Of(plan, "hist.2020.covid").Count(i => i.StageIndex == 0), "the slump is over: not applied");
            List<CatchUpItem> rebound = Of(plan, "hist.2020.covid").ToList();
            Assert.Equal(2, rebound.Count);
            Assert.True(rebound.All(i => i.StageIndex == 1 && i.RemainingDays == 334 && i.Channel == null && i.Global), "334 days, the whole market");
            Assert.Equal(new[] { Segment.MidRange, Segment.LowEnd }, rebound.Select(i => i.DemandSegment).ToArray());
            Assert.True(rebound.All(i => Math.Abs(i.Multiplier - 1.30) < 1e-6));
            Assert.Equal(new[] { "hist.2020.shortage", "hist.2020.covid" }, plan.EntryIds.ToArray(), "in the order of the calendar given");
        }

        [Test]
        public void AC_4_2_StartIn2021_WithTheDefaultSettings_CatchesUpOnlyTheShortage()
        {
            var world = new World();   // disaster is off by default
            CatchUpPlan plan = Plan(world, "2021-01-01", Shortage(), Covid());

            Assert.Equal(new[] { "hist.2020.shortage" }, plan.EntryIds.ToArray(), "the pandemic is not applied");
            Assert.Equal(0, Of(plan, "hist.2020.covid").Count());
            Assert.Equal(699, plan.LongestRemaining("hist.2020.shortage"), "the shortage's global effects have 699 days");
            Assert.True(plan.Items.All(i => i.EntryId == "hist.2020.shortage"));
        }

        [Test]
        public void AC_4_3_StartIn2022_WithGpuOn_CatchesUpTheCryptoBoomOf2021WithNinetyDaysLeft()
        {
            var world = new World();
            CatchUpPlan plan = Plan(world, "2022-01-01", Crypto2017(), Crypto2021());

            CatchUpItem boom = plan.Items.Single();
            Assert.Equal("hist.2021.cryptoboom", boom.EntryId);
            Assert.Equal("gpu.demand", boom.Channel);
            Assert.True(boom.Global);
            Assert.Equal(90, boom.RemainingDays, "2021-04-01 + 365 days = 2022-04-01; 275 days passed before the start");
            Assert.Near(1.45, boom.Multiplier, 1e-6);
            Assert.Equal(0, Of(plan, "hist.2017.cryptoboom").Count(), "the boom of 2017 ended in October 2018");
        }

        [Test]
        public void AC_4_3_WithoutTheGpuFeature_TheCryptoBoomIsNotCaughtUp()
        {
            var world = new World();
            world.FalseConditions.Add("hist.2021.cryptoboom");   // featureActive gpu-product-line is false

            Assert.Equal(0, Plan(world, "2022-01-01", Crypto2021()).Items.Count);
        }

        // ---- what is not caught up ----

        [Test]
        public void AnEntryWithoutCatchUp_IsLeftOut()
        {
            var world = new World();
            CalendarEntry entry = Entry("hist.2020.shortage", "2020-12-01", "shock", false, "[ " + Stage(0, Fire("hist.2020.shortage")) + " ]");

            Assert.Equal(0, Plan(world, "2021-01-01", entry).Items.Count);
        }

        [Test]
        public void AnEntryWhoseAnchorIsNotBeforeTheStart_IsLeftOut()
        {
            var world = new World();
            Assert.Equal(0, Plan(world, "2020-12-01", Shortage()).Items.Count, "on the start day itself it fires as a normal entry");
            Assert.Equal(0, Plan(world, "2020-06-01", Shortage()).Items.Count, "in the future");
            Assert.True(Plan(world, "2020-12-02", Shortage()).Items.Count > 0, "a day later it is the past");
        }

        [Test]
        public void AnEffectThatHasEnded_IsLeftOut()
        {
            var world = new World();
            CatchUpPlan plan = Plan(world, "2023-01-01", Shortage());   // 730 days ended on 2022-12-01; the output effect (540 days) long ago

            Assert.Equal(0, plan.Items.Count);
            Assert.Equal(0, plan.EntryIds.Count);

            CatchUpPlan lastDay = Plan(world, "2022-11-30", Shortage());
            Assert.Equal(1, lastDay.Items.Where(i => i.Channel == "production.unitCost").Single().RemainingDays, "one day left");
            Assert.Equal(0, Plan(world, "2022-12-01", Shortage()).Items.Count(i => i.Channel == "production.unitCost"), "the end date itself is already outside");
        }

        [Test]
        public void AStageThatHasNotBegunAtTheStart_IsLeftOut()
        {
            var world = new World();
            world.Add("hist.2022.export.update", World.Timed("market.rating.cpu", 0.9f, 365, true));
            CalendarEntry entry = Entry("hist.2022.exportcontrol", "2022-10-07", "export", true,
                "[ " + Stage(0, Fire("hist.2022.export.rule")) + ", " + Stage(375, Fire("hist.2022.export.update")) + " ]");
            world.Add("hist.2022.export.rule", World.Timed("market.rating.cpu", 0.95f, 365, true));

            CatchUpPlan plan = Plan(world, "2023-06-01", entry);

            Assert.Equal(new[] { 0 }, plan.Items.Select(i => i.StageIndex).Distinct().ToArray(), "the second stage would begin on 2023-10-17, after the start: it belongs to an entry that is not going to fire");
            Assert.Equal(Sim("2022-10-07") + 365 - Sim("2023-06-01"), plan.Items.Single().RemainingDays);
        }

        [Test]
        public void ACategoryThatIsOff_LeavesTheEntryOut()
        {
            var world = new World();
            world.Enabled.Remove("shock");
            Assert.Equal(0, Plan(world, "2021-01-01", Shortage()).Items.Count);
        }

        [Test]
        public void RolesThatAreNotFound_OrAFalseCondition_LeaveTheEntryOut()
        {
            var world = new World();
            world.RolesOf["hist.2020.shortage"] = new RoleResult { Ok = false, FailedRole = "target" };
            Assert.Equal(0, Plan(world, "2021-01-01", Shortage()).Items.Count);

            var other = new World();
            other.FalseConditions.Add("hist.2020.shortage");
            Assert.Equal(0, Plan(other, "2021-01-01", Shortage()).Items.Count);
        }

        [Test]
        public void AnEventThePackDoesNotHave_AndEffectsOfOtherTypes_AreLeftOut()
        {
            var world = new World();
            world.Effects.Remove("hist.2020.shortage");
            world.Add("hist.2020.shortage.output",
                new CatchUpEffect { Type = "Money", Multiplier = 1.5f, DurationDays = 100 },
                new CatchUpEffect { Type = "AntitrustCase", Multiplier = 1.5f, DurationDays = 100 },
                new CatchUpEffect { Type = CatchUpEffectTypes.FactoryOutput, Multiplier = 0.9f, DurationDays = 0 },
                new CatchUpEffect { Type = CatchUpEffectTypes.FactoryOutput, Multiplier = 0.9f, DurationDays = -5 },
                null);

            CatchUpPlan plan = Plan(world, "2021-01-01", Shortage());

            Assert.Equal(0, plan.Items.Count, "the event of the first fire is unknown; the second has nothing the catch-up understands or nothing timed");
        }

        [Test]
        public void AnEffectWithoutAChannel_IsLeftOut()
        {
            var world = new World();
            world.Add("hist.2021.cryptoboom", new CatchUpEffect { Type = CatchUpEffectTypes.ModifierTimed, Channel = null, Multiplier = 1.4f, DurationDays = 365, GlobalScope = true });

            Assert.Equal(0, Plan(world, "2021-06-01", Crypto2021()).Items.Count);
        }

        [Test]
        public void NothingToDo_GivesAnEmptyPlan_WithoutThrowing()
        {
            var world = new World();
            Assert.Equal(0, CatchUpPlanner.Plan(100, null, world).Items.Count);
            Assert.Equal(0, CatchUpPlanner.Plan(100, new CalendarEntry[0], world).Items.Count);
            Assert.Equal(0, CatchUpPlanner.Plan(100, new[] { Shortage() }, null).Items.Count);
            Assert.Equal(0, CatchUpPlanner.Plan(Sim("2021-01-01"), new CalendarEntry[] { null }, world).Items.Count);
            Assert.Equal(0, new CatchUpPlan().LongestRemaining("nothing"));
        }

        // ---- for whom ----

        [Test]
        public void AnEffectOnTheTarget_IsMadeForEachCompanyOfTheRole_AndAGlobalOneOnce()
        {
            var world = new World();
            CatchUpPlan plan = Plan(world, "2021-01-01", Shortage());

            List<CatchUpItem> output = plan.Items.Where(i => i.Channel == "production.output").ToList();
            Assert.Equal(1, output.Count, "allFabless is one company in this world");
            Assert.Equal("ai:10", output[0].CompanyKey);
            Assert.False(output[0].Global);
            Assert.Equal(540 - 31, output[0].RemainingDays);
            Assert.Near(0.90, output[0].Multiplier, 1e-6);

            world.RolesOf["hist.2020.shortage"] = new RoleResult();
            world.RolesOf["hist.2020.shortage"].Targets[RoleKinds.AllFabless] = new List<string> { "ai:10", "ai:11", "ai:12" };
            CatchUpPlan three = Plan(world, "2021-01-01", Shortage());
            Assert.Equal(new[] { "ai:10", "ai:11", "ai:12" }, three.Items.Where(i => i.Channel == "production.output").Select(i => i.CompanyKey).ToArray());
            Assert.Equal(1, three.Items.Count(i => i.Channel == "production.unitCost"), "the global effect once, however many companies");
        }

        [Test]
        public void AModifierOfTheTargetScope_IsMadeForTheCompaniesOfTheRole()
        {
            var world = new World();
            world.Add("hist.2005.archswitch", World.Timed("contract.signChance", 1.25f, 1095, false));
            CalendarEntry entry = Entry("hist.2005.archswitch", "2005-06-06", "oem", true, "[ " + Stage(0, Fire("hist.2005.archswitch", "target")) + " ]");
            world.RolesOf["hist.2005.archswitch"] = new RoleResult();
            world.RolesOf["hist.2005.archswitch"].Targets["target"] = new List<string> { "ai:0" };

            CatchUpPlan plan = Plan(world, "2006-06-06", entry);

            CatchUpItem item = plan.Items.Single();
            Assert.Equal("contract.signChance", item.Channel);
            Assert.False(item.Global);
            Assert.Equal("ai:0", item.CompanyKey);
            Assert.Equal(1095 - 365, item.RemainingDays);
        }

        [Test]
        public void AGlobalEventFiredAtTheGlobalRole_WithATargetScopeEffect_ReachesThePlayer()
        {
            var world = new World();
            world.Add("hist.1995.win95", World.Timed("popularity.gain", 1.1f, 730, false));
            CalendarEntry entry = Entry("hist.1995.win95", "1995-08-24", "boom", true, "[ " + Stage(0, Fire("hist.1995.win95")) + " ]");

            CatchUpItem item = Plan(world, "1996-08-24", entry).Items.Single();

            Assert.Equal("player", item.CompanyKey, "a global-role fire has the player as its target");
            Assert.False(item.Global);
        }

        [Test]
        public void OnlyIf_DecidesWhichCompaniesAFireReaches()
        {
            var world = new World();
            world.Add("hist.2022.export.rule", World.Timed("market.rating.cpu", 0.9f, 365, false));
            CalendarEntry entry = Entry("hist.2022.exportcontrol", "2022-10-07", "export", true,
                "[ " + Stage(0, Fire("hist.2022.export.rule", "allCpuCompanies", "{ 'type': 'hist.PlayerHasCpuAbove', 'ruleId': 'us2022', 'anyCompany': true }")) + " ]");
            world.OnlyIf = (fire, key) => key != "ai:0";

            CatchUpPlan plan = Plan(world, "2023-01-01", entry);

            Assert.Equal(new[] { "player", "ai:10" }, plan.Items.Select(i => i.CompanyKey).ToArray());
            Assert.Equal(new[] { "hist.2022.export.rule@player", "hist.2022.export.rule@ai:0", "hist.2022.export.rule@ai:10" }, world.OnlyIfAsked.ToArray());
        }

        [Test]
        public void AFireWhoseOnlyIfFailsForEveryone_ReachesNobody()
        {
            var world = new World();
            world.Add("hist.x", World.Timed("market.rating.cpu", 0.9f, 365, true));
            CalendarEntry entry = Entry("hist.2022.x", "2022-10-07", "export", true,
                "[ " + Stage(0, Fire("hist.x", "global", "{ 'type': 'hist.IsFabless' }")) + " ]");
            world.OnlyIf = (fire, key) => false;

            Assert.Equal(0, Plan(world, "2023-01-01", entry).Items.Count, "not even the global effect: the fire itself did not reach anybody");
        }

        // ---- demand and its segments ----

        [Test]
        public void ADemandEffect_IsOneItemPerSegment_AndAllIsTheFourSegments()
        {
            var world = new World();
            CalendarEntry covid = Covid();
            world.Enabled.Add("disaster");

            CatchUpPlan plan = Plan(world, "2020-04-01", covid);   // the slump is running: 90 days from 2020-03-11, 70 left

            List<CatchUpItem> slump = Of(plan, "hist.2020.covid").ToList();
            Assert.Equal(4, slump.Count);
            Assert.Equal(new[] { Segment.HighEnd, Segment.MidRange, Segment.LowEnd, Segment.Industries }, slump.Select(i => i.DemandSegment).ToArray());
            Assert.True(slump.All(i => i.RemainingDays == 90 - 21 && i.Channel == null && i.Global && i.StageIndex == 0));
            Assert.Equal(0, slump.Count(i => i.StageIndex == 1), "the rebound has not begun");
        }

        [Test]
        public void DemandSegments_AreTheNamedOneOrAllFour()
        {
            Assert.Equal(new[] { Segment.HighEnd }, CatchUpPlanner.DemandSegments("highend").ToArray());
            Assert.Equal(new[] { Segment.LowEnd }, CatchUpPlanner.DemandSegments("LowEnd").ToArray());
            Assert.Equal(4, CatchUpPlanner.DemandSegments("all").Count);
            Assert.Equal(4, CatchUpPlanner.DemandSegments(null).Count);
            Assert.Equal(4, CatchUpPlanner.DemandSegments("").Count);
            Assert.Equal(4, CatchUpPlanner.DemandSegments("nowhere").Count, "an unknown name is the whole market, like a missing one");
        }

        // ---- the intensity ----

        [Test]
        public void TheIntensity_ScalesTheDeviation_OfAScalableEffectOnly_AC_10_1()
        {
            var world = new World { Intensity = 0.5f };
            world.Add("hist.2008.lehman", World.Demand("all", 0.80f, 365), World.Demand("highend", 0.80f, 365, false));
            CalendarEntry entry = Entry("hist.2008.lehman", "2008-09-15", "macro", true, "[ " + Stage(0, Fire("hist.2008.lehman")) + " ]");

            CatchUpPlan plan = Plan(world, "2009-01-01", entry);

            Assert.True(plan.Items.Where(i => i.DemandSegment != Segment.HighEnd).Take(3).All(i => Math.Abs(i.Multiplier - 0.90) < 1e-6), "x 0.80 at intensity 0.5 is x 0.90");
            Assert.Equal(5, plan.Items.Count, "all four segments of the scalable effect and one of the fixed");
            Assert.Near(0.80, plan.Items.Last().Multiplier, 1e-6, "the effect that is not scalable keeps its number");
        }

        [Test]
        public void IntensityMath_ScalesTheDeviationFromOne()
        {
            Assert.Near(0.90, IntensityMath.Scale(0.80f, 0.5f, true), 1e-6);
            Assert.Near(0.80, IntensityMath.Scale(0.80f, 1.0f, true), 1e-6);
            Assert.Near(0.70, IntensityMath.Scale(0.80f, 1.5f, true), 1e-6);
            Assert.Near(1.175, IntensityMath.Scale(1.35f, 0.5f, true), 1e-6);
            Assert.Near(1.0, IntensityMath.Scale(1.0f, 1.5f, true), 1e-6, "no deviation, nothing to scale");
            Assert.Near(0.80, IntensityMath.Scale(0.80f, 0.5f, false), 1e-6, "not scalable");
            Assert.Near(0.0, IntensityMath.Scale(0.2f, 1.5f, true), 1e-6, "a multiplier is never negative");
            Assert.True(float.IsNaN(IntensityMath.Scale(float.NaN, 1f, true)));
            Assert.Near(0.8, IntensityMath.Scale(0.8f, float.NaN, true), 1e-6, "an intensity that is not a number changes nothing");
            Assert.Near(0.8, IntensityMath.Scale(0.8f, float.PositiveInfinity, true), 1e-6);
        }

        // ---- the details of the items ----

        [Test]
        public void TheDaysLeft_AreCountedFromTheStartOfTheStage_AndTheJitterIsNotUsed()
        {
            var world = new World();
            CalendarEntry entry = Entry("hist.1995.win95", "1995-08-24", "boom", true, "[ " + Stage(0, Fire("hist.1995.win95")) + " ]");
            entry.JitterDays = 10;
            world.Add("hist.1995.win95", World.Demand("midrange", 1.25f, 730));

            CatchUpItem item = Plan(world, "1996-08-24", entry).Items.Single();

            Assert.Equal(730 - 366, item.RemainingDays, "1995-08-24 to 1996-08-24 is 366 days (a leap year)");
            Assert.Equal(Segment.MidRange, item.DemandSegment);
            Assert.Equal("hist.1995.win95", item.EventId);
            Assert.Equal(0, item.StageIndex);
        }

        [Test]
        public void TheLongestRemaining_IsWhatTheLetterSaysOfAnEntry()
        {
            var world = new World();
            world.Enabled.Add("disaster");
            CatchUpPlan plan = Plan(world, "2021-01-01", Shortage(), Covid());

            Assert.Equal(699, plan.LongestRemaining("hist.2020.shortage"), "the unit cost lasts longest; the factory output (509 days) ends sooner");
            Assert.Equal(334, plan.LongestRemaining("hist.2020.covid"));
        }

        // ---- the game side ----

        [Test]
        public void TheGameSide_WritesTheEntriesWithTheReasonAndSendsOneLetter()
        {
            string code = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "CatchUpService.cs")), true);

            Assert.True(Regex.IsMatch(code, @"ModModifiers\.Add\(\s*HistIds\.Feature\s*,\s*channel\s*,\s*scope\s*,\s*item\.Multiplier\s*,\s*item\.RemainingDays\s*,\s*HistIds\.CatchUpReasonKey"),
                "ModModifiers.Add(feature, channel, scope, multiplier', remaining days, reason hist.catchup)");
            Assert.True(code.Contains("ModChannels.MarketDemand(item.DemandSegment)"), "the demand channel of the segment comes from the core");
            Assert.True(code.Contains("ModifierScope.Global()") && code.Contains("ModifierScope.Company(item.CompanyKey)"), "the scope: the whole market or the company");
            Assert.True(code.Contains("catchUpEntryIds.Add"), "the ids of the entries are kept in the save");
            Assert.Equal(1, Regex.Matches(code, @"ModMail\.Send\(").Count, "one letter");
            Assert.True(Regex.IsMatch(code, @"Tag\s*=\s*HistIds\.CatchUpMailTag") && Regex.IsMatch(code, @"Priority\s*=\s*MailPriority\.Normal"), "tag hist:catchup, priority Normal");
            Assert.True(code.Contains("made > 0"), "no effects, no letter");
            Assert.True(code.Contains("CalendarContent.LoadedPacks() == null) return false"), "the pack is not read yet: try again later");
        }

        [Test]
        public void TheReasonTheTagAndTheKeysOfTheLetter_AreTheOnesOfTheTask()
        {
            Assert.Equal("hist.catchup", HistIds.CatchUpReasonKey);
            Assert.Equal("hist:catchup", HistIds.CatchUpMailTag);
            Assert.Equal("production.output", CatchUpEffectTypes.FactoryOutputChannel);
            HistFixture.WithIds(() =>
            {
                Assert.True(PTMod.Core.Logic.Ids.ModIds.IsTagAllowed(HistIds.Feature, HistIds.CatchUpMailTag), "a tag of a mod mail starts with the prefix of the feature and a colon");
                Assert.True(PTMod.Core.Logic.Ids.ModIds.IsL10nKeyAllowed(HistIds.Feature, HistIds.CatchUpReasonKey));
            });
        }

        [Test]
        public void TheFactoryOutputChannel_IsTheChannelOfTheCore()
        {
            string core = SourceScan.Strip(SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Core", "Modifiers", "ModChannels.cs")), false);
            Assert.True(Regex.IsMatch(core, @"ProductionOutput\s*=\s*""" + Regex.Escape(CatchUpEffectTypes.FactoryOutputChannel) + @""""), "ModChannels.ProductionOutput");
        }

        [Test]
        public void TheCatchUp_RunsForANewGameOnly_AndWaitsForThePack()
        {
            string scheduler = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "CalendarScheduler.cs")), true);

            Assert.Equal(1, Regex.Matches(scheduler, @"_catchUpPending\s*=\s*true").Count, "only the setup of a new game asks for a catch-up");
            Match setUp = Regex.Match(scheduler, @"private void SetUpNewGame\(\)\s*\{(?<body>.*?)\n        \}", RegexOptions.Singleline);
            Assert.True(setUp.Success && setUp.Groups["body"].Value.Contains("_catchUpPending = true"), "in SetUpNewGame");
            Assert.True(scheduler.Contains("TryCatchUp(today.Value)") && scheduler.Contains("_catchUp.Run(today)"), "tried at the fast-forward, at GameReady and then daily");
            Assert.True(scheduler.Contains("CatchUpGiveUpDays"), "and given up after a month");
        }

        [Test]
        public void TheLettersAndTheReasonHaveTextsInBothLanguages()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                string text = SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "L10n", language, "historical-events.json"));
                PTMod.Core.Logic.Json.JsonNode node;
                PTMod.Core.Logic.Json.JsonError error;
                Assert.True(PTMod.Core.Logic.Json.ModJson.TryParse(text, out node, out error), language + ": " + error);
                var table = (PTMod.Core.Logic.Json.JsonObject)node;
                foreach (string key in new[] { "hist.catchup", "hist.catchup.title", "hist.catchup.body", "hist.catchup.line" })
                {
                    Assert.True(table.ContainsKey(key), language + " lacks " + key);
                }
                Assert.True(table.GetString("hist.catchup.body").Contains("{0}"), language + ": the body shows the list");
                Assert.True(table.GetString("hist.catchup.line").Contains("{0}") && table.GetString("hist.catchup.line").Contains("{1}"), language + ": a line has a title and the time left");
                Assert.True(table.GetString("hist.catchup").Contains("{0}"), language + ": the reason shows the event");
                Assert.True(table.ContainsKey("hist.catchup.days.one"), language + " plural one");
                Assert.True(table.ContainsKey(language == "ru" ? "hist.catchup.days.few" : "hist.catchup.days.other"), language + " plural form");
                if (language == "ru") Assert.True(table.ContainsKey("hist.catchup.days.many"), "ru plural many");
            }
        }

        private static int Sim(string iso)
        {
            SimpleDate date;
            Assert.True(SimpleDate.TryParse(iso, out date), iso);
            return date.Days;
        }

        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "HistoricalEvents");
        }
    }
}
