using PTMod.Features.ResearchTreeUx;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// FR-3 item 4 on the clock of "the cursor stayed long enough" (TSK-006): it fires once after <c>hoverSeenSeconds</c>, not at a short pass
    /// of the cursor, and starts again at the next entry. The component of Unity that feeds it is checked by the owner in the game.
    /// </summary>
    [Covers(typeof(HoverSeenTimer))]
    public class HoverSeenTimerTests
    {
        [Test]
        public void StayingLongEnough_FiresOnce_AtTheMomentTheTimeIsUp()
        {
            var timer = new HoverSeenTimer(0.5f);
            timer.Enter();

            Assert.False(timer.Advance(0.2f));
            Assert.False(timer.Advance(0.2f));
            Assert.True(timer.Advance(0.2f), "0.6 s of 0.5 s");
            Assert.False(timer.Advance(0.2f), "only once per stay");
            Assert.False(timer.Advance(5f));
            Assert.True(timer.HasFired);
        }

        [Test]
        public void ReachingTheTimeExactly_Fires()
        {
            var timer = new HoverSeenTimer(0.5f);
            timer.Enter();

            Assert.False(timer.Advance(0.25f));
            Assert.True(timer.Advance(0.25f));
        }

        [Test]
        public void AShortPassOfTheCursor_FiresNothing()
        {
            var timer = new HoverSeenTimer(0.5f);

            timer.Enter();
            Assert.False(timer.Advance(0.3f));
            timer.Exit();
            Assert.False(timer.Advance(0.3f), "the cursor is away: the time does not run");

            timer.Enter();
            Assert.False(timer.Advance(0.3f), "the count started again; 0.3 s is not enough, though 0.6 s were spent in all");
            Assert.False(timer.HasFired);
        }

        [Test]
        public void EnteringAgainAfterAStay_CanFireAgain()
        {
            var timer = new HoverSeenTimer(0.5f);
            timer.Enter();
            Assert.True(timer.Advance(1f));
            timer.Exit();

            timer.Enter();
            Assert.False(timer.HasFired);
            Assert.True(timer.Advance(0.5f));
        }

        [Test]
        public void WithoutEnteringFirst_NothingFires()
        {
            var timer = new HoverSeenTimer(0.5f);

            Assert.False(timer.IsHovering);
            Assert.False(timer.Advance(10f));
        }

        [Test]
        public void ElapsedCountsOnlyTheCurrentStay()
        {
            var timer = new HoverSeenTimer(2f);
            timer.Enter();
            timer.Advance(0.75f);
            Assert.Near(0.75, timer.Elapsed);

            timer.Exit();
            Assert.Near(0.0, timer.Elapsed);
            Assert.False(timer.IsHovering);
        }

        [Test]
        public void ZeroSeconds_FiresOnTheFirstFrameOfTheStay()
        {
            var timer = new HoverSeenTimer(0f);
            timer.Enter();

            Assert.True(timer.Advance(0f));
        }

        [Test]
        public void ABadTime_FallsBackToTheDefaultOfTheSpec()
        {
            Assert.Near(0.5, HoverSeenTimer.DefaultSeconds);
            Assert.Near(0.5, new HoverSeenTimer(-1f).Seconds);
            Assert.Near(0.5, new HoverSeenTimer(float.NaN).Seconds);
            Assert.Near(0.8, new HoverSeenTimer(0.8f).Seconds, 1e-6);
        }

        [Test]
        public void ABadFrameTime_IsIgnored()
        {
            var timer = new HoverSeenTimer(0.5f);
            timer.Enter();

            Assert.False(timer.Advance(-3f));
            Assert.False(timer.Advance(float.NaN));
            Assert.Near(0.0, timer.Elapsed);
            Assert.True(timer.Advance(0.5f));
        }
    }
}
