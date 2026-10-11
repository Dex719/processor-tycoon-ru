using PTMod.Features.ContractTransparency;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>TSK-004: the matrix of the confirmation modes against the risk flags (FR-4 items 1-3) and the translation of the stored setting.</summary>
    [Covers(typeof(ConfirmPolicy), typeof(ConfirmMode), typeof(RiskFlags))]
    public class ConfirmPolicyTests
    {
        private static readonly RiskFlags[] Single =
        {
            RiskFlags.CapacityShortNow,
            RiskFlags.Overcommit,
            RiskFlags.NegativeProfit,
            RiskFlags.BelowAutomationMargin,
            RiskFlags.LowActualChance,
            RiskFlags.BreakFineExceedsCash,
            RiskFlags.AutoRejectWillWithdraw,
        };

        private static readonly RiskFlags All =
            RiskFlags.CapacityShortNow | RiskFlags.Overcommit | RiskFlags.NegativeProfit | RiskFlags.BelowAutomationMargin |
            RiskFlags.LowActualChance | RiskFlags.BreakFineExceedsCash | RiskFlags.AutoRejectWillWithdraw;

        [Test]
        public void Always_AsksEvenWithoutRisks()
        {
            Assert.True(ConfirmPolicy.NeedsConfirmation(ConfirmMode.Always, RiskFlags.None));
            foreach (RiskFlags flag in Single) Assert.True(ConfirmPolicy.NeedsConfirmation(ConfirmMode.Always, flag), flag.ToString());
            Assert.True(ConfirmPolicy.NeedsConfirmation(ConfirmMode.Always, All));
        }

        [Test]
        public void Risky_AsksOnlyWhenThereIsARisk_AnyOfTheSeven()
        {
            Assert.False(ConfirmPolicy.NeedsConfirmation(ConfirmMode.Risky, RiskFlags.None));
            foreach (RiskFlags flag in Single) Assert.True(ConfirmPolicy.NeedsConfirmation(ConfirmMode.Risky, flag), flag.ToString());
            Assert.True(ConfirmPolicy.NeedsConfirmation(ConfirmMode.Risky, All));
            Assert.True(ConfirmPolicy.NeedsConfirmation(ConfirmMode.Risky, RiskFlags.LowActualChance | RiskFlags.NegativeProfit));
        }

        [Test]
        public void Never_DoesNotAsk_WhateverTheRisks()
        {
            Assert.False(ConfirmPolicy.NeedsConfirmation(ConfirmMode.Never, RiskFlags.None));
            foreach (RiskFlags flag in Single) Assert.False(ConfirmPolicy.NeedsConfirmation(ConfirmMode.Never, flag), flag.ToString());
            Assert.False(ConfirmPolicy.NeedsConfirmation(ConfirmMode.Never, All));
        }

        [Test]
        public void AModeThatIsNotOneOfTheThree_CountsAsTheDefault()
        {
            var unknown = (ConfirmMode)42;

            Assert.False(ConfirmPolicy.NeedsConfirmation(unknown, RiskFlags.None));
            Assert.True(ConfirmPolicy.NeedsConfirmation(unknown, RiskFlags.Overcommit));
        }

        [Test]
        public void TheSevenFlagsAreSevenDifferentBits()
        {
            int seen = 0;
            foreach (RiskFlags flag in Single)
            {
                int bit = (int)flag;
                Assert.True(bit != 0 && (bit & (bit - 1)) == 0, flag + " is a single bit");
                Assert.Equal(0, seen & bit, flag + " is a new bit");
                seen |= bit;
            }
            Assert.Equal((int)All, seen);
            Assert.Equal(0, (int)RiskFlags.None);
        }

        [Test]
        public void FromSetting_ReadsTheStoredNumber_AndFallsBackToOnlyRisky()
        {
            Assert.Equal(ConfirmMode.Always, ConfirmPolicy.FromSetting(0));
            Assert.Equal(ConfirmMode.Risky, ConfirmPolicy.FromSetting(1));
            Assert.Equal(ConfirmMode.Never, ConfirmPolicy.FromSetting(2));
            Assert.Equal(ConfirmMode.Risky, ConfirmPolicy.FromSetting(-1));
            Assert.Equal(ConfirmMode.Risky, ConfirmPolicy.FromSetting(3));
            Assert.Equal(ConfirmMode.Risky, ConfirmPolicy.DefaultMode);
        }

        [Test]
        public void ToSetting_IsTheInverseOfFromSetting()
        {
            foreach (ConfirmMode mode in new[] { ConfirmMode.Always, ConfirmMode.Risky, ConfirmMode.Never })
            {
                Assert.Equal(mode, ConfirmPolicy.FromSetting(ConfirmPolicy.ToSetting(mode)));
            }
            Assert.Equal(0, ConfirmPolicy.ToSetting(ConfirmMode.Always));
            Assert.Equal(1, ConfirmPolicy.ToSetting(ConfirmMode.Risky));
            Assert.Equal(2, ConfirmPolicy.ToSetting(ConfirmMode.Never));
        }

        // ---- the switch "don't ask again for safe offers" of the review screen (FR-4 item 4) ----

        [Test]
        public void TheDontAskSwitch_IsShownOnlyWhereEveryOfferIsAskedAbout()
        {
            Assert.True(ConfirmPolicy.DontAskIsShown(ConfirmMode.Always));
            Assert.False(ConfirmPolicy.DontAskIsShown(ConfirmMode.Risky), "the safe offers go through already");
            Assert.False(ConfirmPolicy.DontAskIsShown(ConfirmMode.Never));
            Assert.False(ConfirmPolicy.DontAskIsShown((ConfirmMode)9), "an unknown mode counts as the default, 'only risky'");
        }

        [Test]
        public void TheDontAskSwitch_On_TakesAlwaysToOnlyRisky_AndRemembersItDidSo()
        {
            bool changed;
            ConfirmMode next = ConfirmPolicy.AfterDontAsk(ConfirmMode.Always, true, false, out changed);

            Assert.Equal(ConfirmMode.Risky, next);
            Assert.True(changed);
        }

        [Test]
        public void TheDontAskSwitch_Off_GivesTheModeBack_OnlyIfTheSwitchTookIt()
        {
            bool changed;
            Assert.Equal(ConfirmMode.Always, ConfirmPolicy.AfterDontAsk(ConfirmMode.Risky, false, true, out changed));
            Assert.False(changed);

            // Another way changed the mode meanwhile (the hub): the switch leaves it alone.
            Assert.Equal(ConfirmMode.Risky, ConfirmPolicy.AfterDontAsk(ConfirmMode.Risky, false, false, out changed));
            Assert.False(changed);
            Assert.Equal(ConfirmMode.Never, ConfirmPolicy.AfterDontAsk(ConfirmMode.Never, false, true, out changed), "the player chose 'never' in the hub: not undone");
            Assert.False(changed);
        }

        [Test]
        public void TheDontAskSwitch_On_InAnotherMode_ChangesNothing()
        {
            bool changed;
            Assert.Equal(ConfirmMode.Never, ConfirmPolicy.AfterDontAsk(ConfirmMode.Never, true, false, out changed));
            Assert.False(changed);
            Assert.Equal(ConfirmMode.Risky, ConfirmPolicy.AfterDontAsk(ConfirmMode.Risky, true, false, out changed));
            Assert.False(changed);
        }
    }
}
