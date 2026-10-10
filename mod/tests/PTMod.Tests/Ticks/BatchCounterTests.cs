using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Ticks;

namespace PTMod.Tests.Ticks
{
    /// <summary>
    /// The batch run counter of P21 (FR-4 items 8-9, TSK-057): <c>IsBatchRun</c> holds for the whole <c>DateController.NextMonth</c>, nested
    /// calls are counted, an exception inside still brings the depth back to 0 (the finalizer), and <c>BatchEnded</c> comes exactly once per
    /// outer run (AC-4.4). <see cref="NextMonth"/> plays the patched method: the prefix enters, the finalizer leaves in every case.
    /// </summary>
    [Covers(typeof(BatchCounter))]
    public class BatchCounterTests
    {
        private readonly BatchCounter counter = new BatchCounter();
        private int ended;
        private readonly List<bool> activeWhenEnded = new List<bool>();

        /// <summary>The patched <c>NextMonth</c>: prefix <c>Enter</c>, the original body, finalizer <c>Leave</c> (also on an exception) and <c>BatchEnded</c> at depth 0.</summary>
        private void NextMonth(Action body)
        {
            counter.Enter();
            try
            {
                body?.Invoke();
            }
            finally
            {
                if (counter.Leave())
                {
                    ended++;
                    activeWhenEnded.Add(counter.IsActive);
                }
            }
        }

        [Test]
        public void New_IsIdle()
        {
            Assert.Equal(0, counter.Depth);
            Assert.False(counter.IsActive, "IsBatchRun is false outside NextMonth");
            Assert.Equal(0, counter.Started);
            Assert.Equal(0, counter.Completed);
        }

        [Test]
        public void OneRun_ActiveInside_EndsOnce()
        {
            bool inside = false;
            NextMonth(() => inside = counter.IsActive);

            Assert.True(inside, "IsBatchRun is true inside the run");
            Assert.False(counter.IsActive, "and false after it");
            Assert.Equal(0, counter.Depth);
            Assert.Equal(1, ended);
            Assert.Equal(1, counter.Started);
            Assert.Equal(1, counter.Completed);
        }

        [Test]
        public void EnterAndLeave_ReportOnlyTheOuterRun()
        {
            Assert.True(counter.Enter(), "the outer entry");
            Assert.False(counter.Enter(), "a nested entry is not a new run");
            Assert.Equal(2, counter.Depth);
            Assert.False(counter.Leave(), "the nested run ends, the outer goes on");
            Assert.True(counter.IsActive);
            Assert.True(counter.Leave(), "the outer run ends: report it");
            Assert.Equal(0, counter.Depth);
            Assert.Equal(1, counter.Started);
            Assert.Equal(1, counter.Completed);
        }

        [Test]
        public void Ac44_NestedRun_ExceptionInTheInner_DepthZero_EndedOnce()
        {
            int depthInInner = -1;

            // The inner call throws; nobody catches it inside, so it leaves both runs. Both finalizers run.
            Assert.Throws<InvalidOperationException>(() => NextMonth(() =>
            {
                NextMonth(() =>
                {
                    depthInInner = counter.Depth;
                    throw new InvalidOperationException("a game handler failed inside NextMonth");
                });
            }));

            Assert.Equal(2, depthInInner, "the nested entry is counted (depth 2)");
            Assert.Equal(0, counter.Depth, "depth 0 after both calls");
            Assert.False(counter.IsActive, "IsBatchRun is false after both calls");
            Assert.Equal(1, ended, "BatchEnded is raised once");
            Assert.Equal(new[] { false }, activeWhenEnded.ToArray(), "BatchEnded handlers already see IsBatchRun = false");
            Assert.Equal(0, counter.UnbalancedLeaves);
        }

        [Test]
        public void NestedRun_ExceptionCaughtByTheOuter_RunGoesOn_EndedOnceAtTheOuterEnd()
        {
            var seen = new List<bool>();
            NextMonth(() =>
            {
                try
                {
                    NextMonth(() => throw new InvalidOperationException("inner"));
                }
                catch (InvalidOperationException)
                {
                    seen.Add(counter.IsActive);   // still inside the outer run
                }
                Assert.Equal(0, ended, "the inner end is not the end of the run");
                seen.Add(counter.IsActive);
            });

            Assert.Equal(new[] { true, true }, seen.ToArray());
            Assert.Equal(1, ended);
            Assert.False(counter.IsActive);
        }

        [Test]
        public void ExceptionInASingleRun_StillEndsTheRun()
        {
            Assert.Throws<InvalidOperationException>(() => NextMonth(() => throw new InvalidOperationException("boom")));

            Assert.Equal(0, counter.Depth);
            Assert.False(counter.IsActive);
            Assert.Equal(1, ended);
        }

        [Test]
        public void Ac43_EveryDayOfTheRun_SeesTheBatch_AndTheEndComesAfterTheLastDay()
        {
            // NextMonth plays up to 31 days in one frame; every day handler sees IsBatchRun, BatchEnded follows the last day once.
            var days = new List<bool>();
            NextMonth(() =>
            {
                for (int day = 0; day < 31; day++)
                {
                    days.Add(counter.IsActive);
                    Assert.Equal(0, ended, "no BatchEnded during the run (day " + day + ")");
                }
            });

            Assert.Equal(31, days.Count);
            Assert.True(days.TrueForAll(active => active), "IsBatchRun on every day of the run");
            Assert.Equal(1, ended);
        }

        [Test]
        public void DeepNesting_ExceptionAtTheBottom_EndedOnce()
        {
            Action level = null;
            int depth = 0;
            level = () =>
            {
                depth++;
                if (depth < 5) NextMonth(level);
                else throw new InvalidOperationException("bottom");
            };

            Assert.Throws<InvalidOperationException>(() => NextMonth(level));

            Assert.Equal(0, counter.Depth);
            Assert.Equal(1, ended);
            Assert.Equal(1, counter.Started);
        }

        [Test]
        public void ConsecutiveRuns_EachEndsOnce()
        {
            NextMonth(null);
            NextMonth(() => { });
            Assert.Throws<InvalidOperationException>(() => NextMonth(() => throw new InvalidOperationException("x")));

            Assert.Equal(3, ended);
            Assert.Equal(3, counter.Started);
            Assert.Equal(3, counter.Completed);
            Assert.Equal(0, counter.Depth);
        }

        [Test]
        public void LeaveWithoutEnter_IsIgnored_AndCounted()
        {
            Assert.False(counter.Leave(), "nothing to end");
            Assert.False(counter.Leave());
            Assert.Equal(0, counter.Depth, "the depth never goes below 0");
            Assert.False(counter.IsActive);
            Assert.Equal(2, counter.UnbalancedLeaves);
            Assert.Equal(0, counter.Completed);

            NextMonth(null);
            Assert.Equal(1, ended, "a later run works as usual");
        }

        [Test]
        public void Reset_DropsAnOpenRun_WithoutAnEnd()
        {
            counter.Enter();
            counter.Enter();

            Assert.True(counter.Reset(), "a run was open");
            Assert.Equal(0, counter.Depth);
            Assert.False(counter.IsActive);
            Assert.Equal(1, counter.Abandoned);
            Assert.Equal(0, counter.Completed, "an abandoned run is not an ended one");

            Assert.False(counter.Reset(), "nothing open now");
            Assert.Equal(1, counter.Abandoned);

            // The finalizers of the abandoned run may still come: they are ignored, the next run is counted from 0.
            Assert.False(counter.Leave());
            NextMonth(null);
            Assert.Equal(1, ended);
            Assert.Equal(2, counter.Started);
        }

        [Test]
        public void ToString_ShowsTheDepth()
        {
            counter.Enter();
            Assert.True(counter.ToString().Contains("depth 1"), counter.ToString());
        }
    }
}
