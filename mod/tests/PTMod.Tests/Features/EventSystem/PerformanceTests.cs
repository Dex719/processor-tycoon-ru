using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.EventSystem;
using PTMod.Tests.Conditions;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// TSK-027 (NFR-1): the day of the engine against its budget, on the logic without the game. The world is the one of the task: the year 2000, the player and 13 AI
    /// companies, every one with a revenue history, a fab and a CPU on the market; the starter set of ten events; the real scheduler, the real effect runtime with the
    /// nine standard types, the aggregator over the core's own channel engine and the journal; no UI (the letters, notifications and windows are the game's side and the
    /// timer takes them out anyway). The days are run through <c>EventScheduler.ProcessDay</c> with the <see cref="DayTimer"/> of the engine on the real clock.
    /// <para>
    /// A day with a firing is made certain, not left to the lottery: every tenth day one of the ten events is queued for the next day (<c>EventBus.Fire</c> with a delay),
    /// so <c>ProcessDay</c> creates its instances itself; the lottery fires others on top. The year 2000 is run and thrown away (the JIT of this process), then the years
    /// 2001 to 2005 are measured. The assertion is the budget: the mean of a day without a firing at most 0.3 ms, a day with a firing at most 3 ms. A hiccup of the machine (a collection
    /// of the garbage, another process) can spoil one day, so the measurement is repeated up to three times and the first run that keeps the budget counts; the numbers
    /// of the best run are printed with the test. The measurement in the game itself (<c>ModTicks.StatsOf</c> and the line the engine writes while the log is verbose) is the
    /// owner's, see the Evidence of TSK-027.
    /// </summary>
    [Covers(typeof(EventScheduler), typeof(SchedulerOptions), typeof(DayTimer), typeof(DayTimings))]
    public class PerformanceTests
    {
        private const int AiCompanies = 13;
        private const int FirstMeasuredYear = 2001;
        private const int LastMeasuredYear = 2005;
        private const int Attempts = 3;

        /// <summary>One run: the windows (one per game year) and the totals.</summary>
        private sealed class Run
        {
            public readonly List<DayTimings> Years = new List<DayTimings>();
            public int FiringDays;
            public int Days;
            public int Instances;
            public int Journal;
            public double QuietMean;
            public double FiringMax;
            public bool WithinBudget;

            public string Describe()
            {
                return string.Join("\n    ", Years.Select(y => y.Format()).ToArray());
            }
        }

        private static IGameState StateOf()
        {
            var state = new FakeGameState();
            state.Player.CpuOnMarket = true;
            state.Player.ResearchActive = true;
            for (int i = 0; i < AiCompanies; i++)
            {
                FakeGameState.FakeCompany ai = state.Ai(i);
                ai.CpuOnMarket = true;
                ai.ResearchActive = true;
            }
            return state;
        }

        private static FakeWorld WorldOf()
        {
            FakeWorld world = FakeWorld.WithCompanies(AiCompanies);
            world.State = StateOf();
            var keys = new List<string>(world.CompanyKeys);
            for (int i = 0; i < keys.Count; i++)
            {
                world.Add(keys[i], lines: 2 + i, share: 1f / keys.Count, revenue: 5000000f * (i + 1));
                world.RevenueHistory.Add(keys[i]);
                world.Fabs.Add(keys[i]);
            }
            return world;
        }

        private static Run Simulate(PackCatalog catalog, uint seed)
        {
            var run = new Run();
            var timer = new DayTimer(() => true, run.Years.Add);

            // the config of the starter set, with room for the choices of the player (nobody answers them in this test; they time out on their own)
            EventConfig real = catalog.Config;
            CategoryDefinition universal = real.Category("universal");
            var fake = new FakeCatalog(FakeCatalog.ConfigWith(12, FakeCatalog.Category("universal", universal.DailyChance, universal.MinGapDays, universal.MaxActive)),
                catalog.All.ToArray());
            FakeWorld world = WorldOf();
            var rig = new EffectsRig(fake, world, seed, null, timer);
            foreach (string key in world.CompanyKeys) rig.Game.Revenue[key] = world.QuarterlyRevenue(key);

            string[] ids = catalog.All.Select(d => d.id).ToArray();
            int start = Defs.Day(FirstMeasuredYear - 1, 1, 1);   // the year 2000 is the warm-up
            int next = 0;
            int total = Defs.Day(LastMeasuredYear + 1, 1, 1) - start;
            for (int i = 0; i < total; i++)
            {
                int day = start + i;
                if (i % 10 == 5)
                {
                    // queued today, created by ProcessDay tomorrow
                    EventDefinition queued = catalog.Get(ids[next++ % ids.Length]);
                    string company = queued.targets.type == EventTargetType.RandomAI ? CompanyKey.ForAi(i % AiCompanies) : CompanyKey.Player;
                    rig.World.Today = day;
                    world.Today = day;
                    rig.Scheduler.Fire(queued.id, new EventFireContext { CompanyKey = company, DelayDays = 1, IgnoreConditions = true });
                }
                world.Today = day;
                rig.Scheduler.ProcessDay(day);
            }
            timer.CloseWindow();
            run.Years.RemoveAll(year => year.Year < FirstMeasuredYear);   // the warm-up year is thrown away

            foreach (DayTimings year in run.Years)
            {
                run.Days += year.Days;
                run.FiringDays += year.FiringDays;
            }
            run.Instances = rig.Runtime.Instances.Count;
            run.Journal = rig.Log.Entries.Count;
            double quietTotal = run.Years.Sum(y => y.QuietTotalMs);
            int quietDays = run.Years.Sum(y => y.QuietDays);
            run.QuietMean = quietDays > 0 ? quietTotal / quietDays : 0.0;
            run.FiringMax = run.Years.Count > 0 ? run.Years.Max(y => y.FiringMaxMs) : 0.0;
            run.WithinBudget = run.Years.All(y => y.WithinBudget);
            Assert.Equal(0, rig.Faults.Count, "no fault in the run:\n  " + string.Join("\n  ", rig.Faults.ToArray()));
            return run;
        }

        [Test]
        public void NFR_1_ADayOfTheStarterSet_With13AiCompanies_KeepsTheBudget_AnOrdinaryDayAndADayWithAFiring()
        {
            EventTestKit.WithStarterCatalog((env, log, catalog) =>
            {
                Run best = null;
                var attempts = new List<Run>();
                for (int attempt = 0; attempt < Attempts; attempt++)
                {
                    Run run = Simulate(catalog, 777u + (uint)attempt);
                    attempts.Add(run);
                    if (best == null || (run.WithinBudget && !best.WithinBudget) || (run.WithinBudget == best.WithinBudget && run.FiringMax < best.FiringMax)) best = run;
                    if (run.WithinBudget) break;
                }

                Console.WriteLine("[perf] NFR-1, the years " + FirstMeasuredYear + " to " + LastMeasuredYear + ", the player and " + AiCompanies + " AI companies, " + best.Days + " days measured, "
                                  + best.FiringDays + " of them with a firing (attempt " + attempts.Count + " of at most " + Attempts + ")");
                Console.WriteLine("[perf] mean of a day without a firing " + best.QuietMean.ToString("0.0000", CultureInfo.InvariantCulture)
                                  + " ms (budget 0.3), the longest day with a firing " + best.FiringMax.ToString("0.0000", CultureInfo.InvariantCulture) + " ms (budget 3)");
                Console.WriteLine("[perf] " + best.Describe());

                Assert.Equal(Defs.Day(LastMeasuredYear + 1, 1, 1) - Defs.Day(FirstMeasuredYear, 1, 1), best.Days, "every measured day was timed once");
                Assert.Equal(LastMeasuredYear - FirstMeasuredYear + 1, best.Years.Count, "one window per game year");
                Assert.True(best.FiringDays >= (LastMeasuredYear - FirstMeasuredYear + 1) * 25, "the test needs its days with a firing (" + best.FiringDays + ")");
                Assert.True(best.Journal > 100, "the engine did its work in the run (journal entries: " + best.Journal + ")");
                Assert.True(best.QuietMean <= DayTimer.QuietMeanBudgetMs,
                    "NFR-1: the mean of a day without a firing is " + best.QuietMean.ToString("0.0000", CultureInfo.InvariantCulture) + " ms, the budget is 0.3 ms\n    " + best.Describe());
                Assert.True(best.FiringMax <= DayTimer.FiringDayBudgetMs,
                    "NFR-1: the longest day with a firing is " + best.FiringMax.ToString("0.0000", CultureInfo.InvariantCulture) + " ms, the budget is 3 ms\n    " + best.Describe());
            });
        }
    }
}
