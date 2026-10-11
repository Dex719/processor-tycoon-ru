using System;
using System.Collections.Generic;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-015 (FR-1, FR-2, AC-1.1 to AC-1.3, edge cases): the letters of the default. "Default risk" goes out once per episode when the balance first falls below
    /// <c>-0.75 D</c>, is forgotten when the balance climbs back above <c>-0.5 D</c>, and goes out again after a new fall; "Company in default" goes out on the day the default
    /// begins, once per episode. The flags are in the save, so a loaded game does not repeat a letter; the part "warning" off sends none; a letter that could not be sent
    /// is tried again.
    /// </summary>
    [Covers(typeof(RecoveryMonitorEngine), typeof(IRecoveryLetters))]
    public class RecoveryMonitorEngineTests
    {
        private sealed class FakeLetters : IRecoveryLetters
        {
            public readonly List<double[]> PreWarnings = new List<double[]>();
            public readonly List<double[]> Defaults = new List<double[]>();
            public bool Accept = true;

            public bool SendPreWarning(double balance, double threshold)
            {
                PreWarnings.Add(new[] { balance, threshold });
                return Accept;
            }

            public bool SendDefault(double balance, double threshold, int daysToBankruptcy)
            {
                Defaults.Add(new[] { balance, threshold, daysToBankruptcy });
                return Accept;
            }
        }

        private sealed class Setup
        {
            public readonly RecoveryRig Rig = new RecoveryRig();
            public readonly FakeLetters Letters = new FakeLetters();
            public readonly RecoveryMonitorEngine Monitor;

            // AC-1.1: normal difficulty, D = 10 000 000, nothing sent yet
            public Setup()
            {
                Rig.Finance.DebtLimit = 10000000.0;
                Rig.Finance.MoneyValue = -7000000.0;
                Monitor = new RecoveryMonitorEngine(Rig.Env, Letters);
            }

            public FakePlayerFinance Finance
            {
                get { return Rig.Finance; }
            }

            public EpisodeData Episode
            {
                get { return Rig.State.episode; }
            }
        }

        // ---- the warning (FR-1) ----

        [Test]
        public void TheWarning_GoesOutWhenTheBalanceFirstFallsBelowThreeQuartersOfTheLimit_AC_1_1()
        {
            var s = new Setup();

            Assert.False(s.Monitor.OnDay(), "-7 000 000 is above -7 500 000: nothing yet");
            Assert.Equal(0, s.Letters.PreWarnings.Count);

            s.Finance.MoneyValue = -7600000.0;
            Assert.True(s.Monitor.OnDay());

            Assert.Equal(1, s.Letters.PreWarnings.Count);
            Assert.Near(-7600000.0, s.Letters.PreWarnings[0][0], 0, "the balance in the letter");
            Assert.Near(-10000000.0, s.Letters.PreWarnings[0][1], 0, "the threshold in the letter is a negative sum, -D");
            Assert.True(s.Episode.preWarningSent, "the flag of the episode is set");
        }

        [Test]
        public void TheLimitOfTheWarning_IsStrict()
        {
            var s = new Setup();
            s.Finance.MoneyValue = -7500000.0;   // exactly -0.75 D

            Assert.False(s.Monitor.OnDay(), "the rule is a balance below the line, not at it");
            s.Finance.MoneyValue = -7500000.01;
            Assert.True(s.Monitor.OnDay());
        }

        [Test]
        public void NoSecondLetter_WhileTheEpisodeLasts_AC_1_2()
        {
            var s = new Setup();
            s.Finance.MoneyValue = -8000000.0;
            Assert.True(s.Monitor.OnDay());

            s.Finance.MoneyValue = -9000000.0;
            Assert.False(s.Monitor.OnDay());
            s.Finance.MoneyValue = -9900000.0;
            Assert.False(s.Monitor.OnDay());

            Assert.Equal(1, s.Letters.PreWarnings.Count, "one letter per episode");
        }

        [Test]
        public void ACompleteRecovery_StartsANewEpisode_AC_1_3()
        {
            var s = new Setup();
            s.Finance.MoneyValue = -8000000.0;
            Assert.True(s.Monitor.OnDay());

            s.Finance.MoneyValue = -4000000.0;   // above -5 000 000
            Assert.False(s.Monitor.OnDay());
            Assert.False(s.Episode.preWarningSent, "the flag is forgotten");

            s.Finance.MoneyValue = -8000000.0;
            Assert.True(s.Monitor.OnDay(), "the next fall sends a new letter");
            Assert.Equal(2, s.Letters.PreWarnings.Count);
        }

        [Test]
        public void ARecoveryThatStopsHalfway_DoesNotStartANewEpisode()
        {
            var s = new Setup();
            s.Finance.MoneyValue = -8000000.0;
            s.Monitor.OnDay();

            s.Finance.MoneyValue = -5500000.0;   // below -5 000 000: not recovered
            s.Monitor.OnDay();
            Assert.True(s.Episode.preWarningSent);

            s.Finance.MoneyValue = -9000000.0;
            Assert.False(s.Monitor.OnDay());
            Assert.Equal(1, s.Letters.PreWarnings.Count);

            s.Finance.MoneyValue = -5000000.0;   // exactly -0.5 D is enough
            s.Monitor.OnDay();
            Assert.False(s.Episode.preWarningSent);
        }

        [Test]
        public void AGameInDefault_GetsNoWarning_ItGetsTheDefaultLetter()
        {
            var s = new Setup();
            s.Finance.MoneyValue = -12000000.0;
            s.Finance.InDefault = true;

            Assert.False(s.Monitor.OnDay());
            Assert.Equal(0, s.Letters.PreWarnings.Count);
        }

        [Test]
        public void TheWarningIsNotRepeated_AfterALoad_BecauseTheFlagIsInTheSave()
        {
            var s = new Setup();
            s.Finance.MoneyValue = -8000000.0;
            s.Monitor.OnDay();

            // a loaded game: the state is read back, the monitor is a new object, the flag is the saved one
            var loaded = new RecoveryMonitorEngine(s.Rig.Env, s.Letters);
            Assert.False(loaded.OnDay());
            Assert.Equal(1, s.Letters.PreWarnings.Count);
        }

        [Test]
        public void WithThePartOff_NoWarningGoesOut_AC_FR_1_4()
        {
            var s = new Setup();
            s.Rig.Off.Add(SubFeature.PreWarning);
            s.Finance.MoneyValue = -9000000.0;

            Assert.False(s.Monitor.OnDay());

            Assert.Equal(0, s.Letters.PreWarnings.Count);
            Assert.False(s.Episode.preWarningSent);
        }

        [Test]
        public void WithThePartOff_AFlagFromBefore_IsStillForgottenOnRecovery()
        {
            var s = new Setup();
            s.Episode.preWarningSent = true;
            s.Rig.Off.Add(SubFeature.PreWarning);

            s.Finance.MoneyValue = -4000000.0;
            s.Monitor.OnDay();

            Assert.False(s.Episode.preWarningSent, "switching the part on later must not find a stale flag");
        }

        [Test]
        public void ALetterThatCouldNotBeSent_IsTriedAgainTomorrow()
        {
            var s = new Setup();
            s.Finance.MoneyValue = -8000000.0;
            s.Letters.Accept = false;

            Assert.False(s.Monitor.OnDay());
            Assert.False(s.Episode.preWarningSent, "not sent, not marked");

            s.Letters.Accept = true;
            Assert.True(s.Monitor.OnDay());
            Assert.True(s.Episode.preWarningSent);
            Assert.Equal(2, s.Letters.PreWarnings.Count, "the two tries");
        }

        [Test]
        public void ADayFarFromTheThreshold_CostsNoReportRead()
        {
            var s = new Setup();
            s.Finance.MoneyValue = 5000000.0;

            for (int i = 0; i < 365; i++) s.Monitor.OnDay();

            Assert.Equal(0, s.Letters.PreWarnings.Count);
            Assert.Equal(0, s.Finance.SalesReads, "the check reads the balance and the limit only (NFR-1)");
        }

        // ---- only the player, only a company that is there (FR-11) ----

        [Test]
        public void WithoutTheCompany_NothingIsDone()
        {
            var s = new Setup();
            s.Finance.Available = false;
            s.Finance.MoneyValue = -9000000.0;

            Assert.False(s.Monitor.OnDay());
            Assert.False(s.Monitor.OnPlayerDefault());
            Assert.Equal(0, s.Letters.PreWarnings.Count + s.Letters.Defaults.Count);
        }

        [Test]
        public void ABankruptCompany_GetsNoLetter()
        {
            var s = new Setup();
            s.Finance.MoneyValue = -12000000.0;
            s.Finance.Bankrupt = true;

            Assert.False(s.Monitor.OnDay());
            Assert.False(s.Monitor.OnPlayerDefault());
            Assert.Equal(0, s.Letters.PreWarnings.Count + s.Letters.Defaults.Count);
        }

        // ---- the default letter (FR-2) ----

        [Test]
        public void TheDefaultLetter_NamesTheBalance_TheThreshold_AndTheDaysLeft()
        {
            var s = new Setup();
            s.Finance.MoneyValue = -10500000.0;
            s.Finance.InDefault = true;
            s.Finance.Days = 0;

            Assert.True(s.Monitor.OnPlayerDefault());

            Assert.Equal(1, s.Letters.Defaults.Count);
            Assert.Near(-10500000.0, s.Letters.Defaults[0][0], 0);
            Assert.Near(-10000000.0, s.Letters.Defaults[0][1], 0);
            Assert.Near(120.0, s.Letters.Defaults[0][2], 0, "the whole of the 120 days is ahead");
            Assert.True(s.Episode.defaultMailSent);
        }

        [Test]
        public void TheDaysLeft_FollowTheCounterOfTheDefault_AndNeverGoBelowZero()
        {
            var s = new Setup();
            s.Finance.InDefault = true;

            s.Finance.Days = 30;
            s.Monitor.OnPlayerDefault();
            Assert.Near(90.0, s.Letters.Defaults[0][2], 0, "AC-2.2: 30 days in, 90 left");

            s.Episode.defaultMailSent = false;
            s.Finance.Days = 500;
            s.Monitor.OnPlayerDefault();
            Assert.Near(0.0, s.Letters.Defaults[1][2], 0);
        }

        [Test]
        public void TheDefaultLetter_GoesOutOncePerEpisode_AndAgainInTheNext()
        {
            var s = new Setup();
            var cycle = new DefaultCycleEngine(s.Rig.Env, new BailoutEngine(s.Rig.Env, new LoanBook(s.Rig.Env)));
            s.Finance.MoneyValue = -12000000.0;
            s.Finance.InDefault = true;

            Assert.True(s.Monitor.OnPlayerDefault());
            Assert.False(s.Monitor.OnPlayerDefault(), "the same episode: no second letter");
            Assert.Equal(1, s.Letters.Defaults.Count);

            // the default ends (the game's own cancel, the early exit, a rescue); the cycle forgets the flags of the episode
            s.Finance.InDefault = false;
            s.Finance.MoneyValue = 1000000.0;
            cycle.AfterTick();
            Assert.False(s.Episode.defaultMailSent);

            s.Finance.InDefault = true;
            s.Finance.MoneyValue = -12000000.0;
            Assert.True(s.Monitor.OnPlayerDefault(), "a new episode, a new letter");
            Assert.Equal(2, s.Letters.Defaults.Count);
        }

        [Test]
        public void ADefaultLetterThatCouldNotBeSent_IsNotMarked()
        {
            var s = new Setup();
            s.Finance.InDefault = true;
            s.Letters.Accept = false;

            Assert.False(s.Monitor.OnPlayerDefault());
            Assert.False(s.Episode.defaultMailSent);

            s.Letters.Accept = true;
            Assert.True(s.Monitor.OnPlayerDefault());
        }

        [Test]
        public void TheDefaultLetter_IsNotHeldBackByTheSwitchOfTheWarning()
        {
            var s = new Setup();
            s.Rig.Off.Add(SubFeature.PreWarning);
            s.Finance.InDefault = true;

            Assert.True(s.Monitor.OnPlayerDefault(), "FR-2 has no switch of its own: the warning part decides only about FR-1");
        }

        [Test]
        public void TheConstructor_RefusesNothing()
        {
            var s = new Setup();
            Assert.Throws<ArgumentNullException>(() => new RecoveryMonitorEngine(null, s.Letters));
            Assert.Throws<ArgumentNullException>(() => new RecoveryMonitorEngine(s.Rig.Env, null));
        }
    }
}
