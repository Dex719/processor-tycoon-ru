using System.Collections.Generic;
using PTMod.Core.Logic.Companies;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// The category lottery (FR-2 items 2, 6, 7; FR-9 item 2; D6): the frequency over a century (AC-2.1), the fixed use of three daily numbers per category,
    /// the pace of a category (<c>minGapDays</c>, <c>maxActive</c>) and the limit of the player's open choices (AC-2.3).
    /// </summary>
    [Covers(typeof(EventScheduler), typeof(Pacing), typeof(ISchedulerCatalog), typeof(ISchedulerRuntime), typeof(SchedulerOptions), typeof(LotteryDraw))]
    public class CategoryLotteryTests
    {
        private const int Start = 3287;   // 1980-01-01

        private static SchedulerRig Universal(float dailyChance, EventDefinition[] events, int minGap = 0, int maxActive = 100, int maxPending = 2)
        {
            EventConfig config = FakeCatalog.ConfigWith(maxPending, FakeCatalog.Category("universal", dailyChance, minGap, maxActive));
            return new SchedulerRig(new FakeCatalog(config, events));
        }

        [Test]
        public void AC_2_1_UniversalAtNormalFrequency_FiresAbout401TimesIn36500Days()
        {
            // Without any blocking: an AI event without choices, no cooldown, no gap, no limit of active events.
            SchedulerRig rig = Universal(0.011f, new[] { Defs.Daily("es.any", "universal", target: EventTargetType.RandomAI) });
            rig.RunDays(Start, 36500);

            int fired = rig.Runtime.FiredCount("es.any");
            Assert.InRange(fired, 401 - 60, 401 + 60, "36 500 days × 0.011 ≈ 401.5 firings, got " + fired);
            Assert.Equal(36500 * EventRandom.NumbersPerCategory, rig.Daily.Draws, "three numbers per day, whatever happened");
        }

        [Test]
        public void FrequencySetting_ScalesTheChance_AndOffMeansNever()
        {
            Assert.Equal(0f, Pacing.FrequencyFactor(0));
            Assert.Equal(0.5f, Pacing.FrequencyFactor(1));
            Assert.Equal(1f, Pacing.FrequencyFactor(2));
            Assert.Equal(1.5f, Pacing.FrequencyFactor(3));
            Assert.Equal(1.5f, Pacing.FrequencyFactor(9), "clamped");
            Assert.Equal(0f, Pacing.FrequencyFactor(-1), "clamped");

            SchedulerRig off = Universal(0.5f, new[] { Defs.Daily("es.any", "universal", target: EventTargetType.RandomAI) });
            off.Frequency = 0;
            off.RunDays(Start, 400);
            Assert.Equal(0, off.Runtime.Fired.Count, "'off' fires nothing");
            Assert.Equal(400 * 3, off.Daily.Draws, "and still takes its numbers");

            SchedulerRig often = Universal(0.1f, new[] { Defs.Daily("es.any", "universal", target: EventTargetType.RandomAI) });
            often.Frequency = 3;
            often.RunDays(Start, 10000);
            Assert.InRange(often.Runtime.Fired.Count, 1500 - 180, 1500 + 180, "0.1 × 1.5 over 10 000 days");
        }

        [Test]
        public void PackFrequency_MultipliesTheChanceOfItsCategories()
        {
            EventConfig config = FakeCatalog.ConfigWith(2, FakeCatalog.Category("shocks", 0.1f, packId: "market-shocks"));
            EventDefinition shock = Defs.Daily("ms.shock", "shocks", target: EventTargetType.RandomAI);
            shock.packId = "market-shocks";
            var catalog = new FakeCatalog(config, shock).Pack("market-shocks", new PackOptions { Frequency = 0.5f });
            var rig = new SchedulerRig(catalog);

            rig.RunDays(Start, 10000);
            Assert.InRange(rig.Runtime.Fired.Count, 500 - 90, 500 + 90, "0.1 × 0.5 over 10 000 days, got " + rig.Runtime.Fired.Count);
        }

        [Test]
        public void EveryCategoryWithDailyChanceEvents_TakesThreeNumbersEveryDay()
        {
            EventConfig config = FakeCatalog.ConfigWith(2,
                FakeCatalog.Category("universal", 0.5f), FakeCatalog.Category("rare", 0.01f), FakeCatalog.Category("empty", 0.5f));
            var catalog = new FakeCatalog(config,
                Defs.Daily("es.u", "universal"),
                Defs.Daily("es.r", "rare", target: EventTargetType.RandomAI),
                Defs.Manual("es.m"));
            var rig = new SchedulerRig(catalog);
            var seen = new List<string>();
            rig.Scheduler.LotteryObserver = (day, category, draw) => seen.Add(category);

            rig.RunDays(Start, 100);
            Assert.Equal(100 * 2 * 3, rig.Daily.Draws, "two categories have DailyChance events; 'empty' has none and takes nothing");
            Assert.Equal(200, seen.Count);
            Assert.Equal("universal", seen[0], "categories in the order of the config");
            Assert.Equal("rare", seen[1]);
        }

        [Test]
        public void NumbersAreTaken_EvenWhenNoEventFits_OrThePackIsFrozen()
        {
            EventDefinition never = Defs.Daily("es.never", "universal");
            never.conditions = Defs.When((key, day) => false);
            SchedulerRig rig = Universal(1f, new[] { never });
            rig.RunDays(Start, 50);
            Assert.Equal(0, rig.Runtime.Fired.Count);
            Assert.Equal(150, rig.Daily.Draws, "a won roll with no fitting event uses the same three numbers");

            SchedulerRig frozen = Universal(1f, new[] { Defs.Daily("es.any", "universal") });
            frozen.FrozenPacks.Add("event-system");
            frozen.RunDays(Start, 50);
            Assert.Equal(0, frozen.Runtime.Fired.Count, "a frozen pack fires nothing");
            Assert.Equal(150, frozen.Daily.Draws, "and its categories still take their numbers");
        }

        [Test]
        public void WeightPicksTheEvent_InProportion()
        {
            SchedulerRig rig = Universal(1f, new[]
            {
                Defs.Daily("es.heavy", "universal", weight: 3f, target: EventTargetType.RandomAI),
                Defs.Daily("es.light", "universal", weight: 1f, target: EventTargetType.RandomAI),
            });
            rig.RunDays(Start, 8000);

            int heavy = rig.Runtime.FiredCount("es.heavy");
            int light = rig.Runtime.FiredCount("es.light");
            Assert.Equal(8000, heavy + light, "one event every day at chance 1");
            Assert.InRange(heavy, 6000 - 200, 6000 + 200, "weight 3 of 4");
        }

        [Test]
        public void MinGapDays_SkipsTheLotteryAfterAnEventOfTheCategory()
        {
            SchedulerRig rig = Universal(1f, new[] { Defs.Daily("es.any", "universal", target: EventTargetType.RandomAI) }, minGap: 45);
            rig.RunDays(Start, 450);

            Assert.Equal(10, rig.Runtime.Fired.Count, "chance 1 with a gap of 45 days: days 0, 45, 90 ... 405");
            for (int i = 1; i < rig.Runtime.Fired.Count; i++)
            {
                Assert.Equal(45, rig.Runtime.Fired[i].Today - rig.Runtime.Fired[i - 1].Today, "exactly minGapDays apart");
            }
            Assert.Equal(Start + 405, rig.State.CategoryLastDay("universal"));
        }

        [Test]
        public void MaxActive_SkipsTheLotteryWhileThatManyEventsAreGoingOn()
        {
            SchedulerRig rig = Universal(1f, new[] { Defs.Daily("es.any", "universal", target: EventTargetType.RandomAI) }, maxActive: 2);
            rig.Runtime.OnFire = plan =>
            {
                rig.Runtime.Fired.Add(plan);
                rig.History.Active.Set(rig.Runtime.NextInstanceId++, 0, plan.Definition.id, "universal", plan.Targets[0], false, true);
                return 1;
            };

            rig.RunDays(Start, 10);
            Assert.Equal(2, rig.Runtime.Fired.Count, "two active events of the category, the third waits");

            rig.History.Active.Remove(1);   // the first one ended
            rig.RunDays(Start + 10, 5);
            Assert.Equal(3, rig.Runtime.Fired.Count, "a slot is free again");
        }

        [Test]
        public void AC_2_3_PlayerWithTwoOpenChoices_TheChoiceEventDoesNotFire_AndTheNumbersAreTheSame()
        {
            EventDefinition choice = Defs.Daily("es.choice", "universal").WithChoices();
            SchedulerRig rig = Universal(1f, new[] { choice });
            rig.History.Active.Set(901, 0, "es.old1", "other", CompanyKey.Player, true, false);
            rig.History.Active.Set(902, 0, "es.old2", "other", CompanyKey.Player, true, false);

            rig.RunDays(Start, 30);
            Assert.Equal(0, rig.Runtime.Fired.Count, "maxPendingChoices = 2 reached: the day is skipped");
            Assert.Equal(30 * 3, rig.Daily.Draws, "the same three numbers per day as on any day");
            Assert.Equal(0, rig.State.Deferred.Count, "the lottery does not queue: it skips the day");
            Assert.True(rig.Infos.Count > 0, "the skip is logged");

            rig.History.Active.Remove(901);   // one choice was made
            rig.RunDay(Start + 30);
            Assert.Equal(1, rig.Runtime.Fired.Count, "a slot is free: the event fires");
            rig.RunDay(Start + 31);
            Assert.Equal(1, rig.Runtime.Fired.Count, "and fills the slot again");
        }

        [Test]
        public void TheLimitOfOpenChoices_ConcernsOnlyThePlayer_AndOnlyEventsWithChoices()
        {
            SchedulerRig rig = Universal(1f, maxPending: 1, events: new[]
            {
                Defs.Daily("es.ai_choice", "universal", target: EventTargetType.RandomAI).WithChoices(),
                Defs.Daily("es.player_news", "universal"),
            });
            rig.History.Active.Set(901, 0, "es.old", "other", CompanyKey.Player, true, false);

            rig.RunDays(Start, 400);
            Assert.True(rig.Runtime.FiredCount("es.ai_choice") > 100, "AI companies decide at once: no limit for them");
            Assert.True(rig.Runtime.FiredCount("es.player_news") > 100, "an event without choices is not limited");
        }

        [Test]
        public void CooldownAndOncePerGame_TakeAnEventOutOfTheLottery()
        {
            EventDefinition once = Defs.Daily("es.once", "universal", weight: 100f);
            once.oncePerGame = true;
            EventDefinition cooled = Defs.Daily("es.cooled", "universal", weight: 100f, cooldownDays: 365);
            EventDefinition filler = Defs.Daily("es.filler", "universal", weight: 0.001f, target: EventTargetType.RandomAI);
            SchedulerRig rig = Universal(1f, new[] { once, cooled, filler });

            rig.RunDays(Start, 730);
            Assert.Equal(1, rig.Runtime.FiredCount("es.once"), "oncePerGame");
            Assert.Equal(2, rig.Runtime.FiredCount("es.cooled"), "once per 365 days in two years");
            Assert.True(rig.State.IsUsedUp("es.once"));
        }
    }
}
