using PTMod.Features.ContractTransparency;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>TSK-016 (FR-10): what happens to a click on "Sign contract". The matrix is small: the mod's own call and the mode "never ask" go through, so does a window the mod cannot read, an open question swallows the click, the rest is asked.</summary>
    [Covers(typeof(SignGatePolicy), typeof(SignInput), typeof(SignAction))]
    public class SignGatePolicyTests
    {
        private static SignInput Click(ConfirmMode mode)
        {
            return new SignInput { Mode = mode, FieldsAvailable = true };
        }

        [Test]
        public void AnOrdinaryClick_IsAskedAbout_InTheModesAlwaysAndOnlyRisky()
        {
            Assert.Equal(SignAction.Ask, SignGatePolicy.Decide(Click(ConfirmMode.Always)));
            Assert.Equal(SignAction.Ask, SignGatePolicy.Decide(Click(ConfirmMode.Risky)), "a deal has no risk model: 'only risky' asks as 'always'");
        }

        [Test]
        public void TheModeNever_LetsTheSignatureThrough()
        {
            Assert.Equal(SignAction.PassThrough, SignGatePolicy.Decide(Click(ConfirmMode.Never)));
        }

        [Test]
        public void TheModsOwnCall_AfterSign_GoesThrough_InEveryMode()
        {
            foreach (ConfirmMode mode in new[] { ConfirmMode.Always, ConfirmMode.Risky, ConfirmMode.Never })
            {
                SignInput input = Click(mode);
                input.Bypass = true;
                input.QuestionOpen = true;
                Assert.Equal(SignAction.PassThrough, SignGatePolicy.Decide(input), mode.ToString());
            }
        }

        [Test]
        public void AWindowWhoseFieldsAreMissing_IsNotAskedAbout_ThePlayerCanStillSign()
        {
            SignInput input = Click(ConfirmMode.Always);
            input.FieldsAvailable = false;

            Assert.Equal(SignAction.PassThrough, SignGatePolicy.Decide(input));
        }

        [Test]
        public void AClickWhileTheQuestionIsOpen_IsSwallowed_SoOneQuestionCannotBecomeTwoDeals()
        {
            SignInput input = Click(ConfirmMode.Always);
            input.QuestionOpen = true;

            Assert.Equal(SignAction.Swallow, SignGatePolicy.Decide(input));
        }

        [Test]
        public void AnUnknownMode_CountsAsTheDefault()
        {
            Assert.Equal(SignAction.Ask, SignGatePolicy.Decide(Click((ConfirmMode)7)));
        }
    }
}
