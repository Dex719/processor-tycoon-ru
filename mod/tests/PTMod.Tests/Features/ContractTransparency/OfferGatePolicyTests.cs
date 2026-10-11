using System;
using PTMod.Features.ContractTransparency;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>TSK-007: the rule of the gate on <c>Contract.AddOffer</c> (FR-3, FR-4, NFR-2) without the game: who passes, when the screen opens, and that the risks are only asked for when needed.</summary>
    [Covers(typeof(OfferGatePolicy), typeof(GateInput), typeof(GateDecision), typeof(GateAction), typeof(OfferSource))]
    public class OfferGatePolicyTests
    {
        private static GateInput Manual(ConfirmMode mode)
        {
            return new GateInput { IsPlayerCpu = true, Mode = mode };
        }

        private static readonly Func<RiskFlags> Safe = () => RiskFlags.None;
        private static readonly Func<RiskFlags> Risky = () => RiskFlags.Overcommit;

        // ---- calls that pass unchanged ----

        [Test]
        public void TheAisOffers_PassWithoutALine_InEveryMode()
        {
            foreach (ConfirmMode mode in new[] { ConfirmMode.Always, ConfirmMode.Risky, ConfirmMode.Never })
            {
                GateDecision decision = OfferGatePolicy.Decide(new GateInput { IsPlayerCpu = false, Mode = mode }, Risky);

                Assert.Equal(GateAction.PassThrough, decision.Action, mode.ToString());
            }
        }

        [Test]
        public void TheAutomation_PassesTagged_AndNeverGetsAScreen()
        {
            foreach (ConfirmMode mode in new[] { ConfirmMode.Always, ConfirmMode.Risky, ConfirmMode.Never })
            {
                GateInput input = Manual(mode);
                input.AutomationRunning = true;

                GateDecision decision = OfferGatePolicy.Decide(input, Risky);

                Assert.Equal(GateAction.PassAndRecord, decision.Action, mode.ToString());
                Assert.Equal(OfferSource.Automation, decision.Source);
            }
        }

        [Test]
        public void Ac42_TheModeAlways_DoesNotOpenAScreenForTheAutomation()
        {
            GateInput input = Manual(ConfirmMode.Always);
            input.AutomationRunning = true;

            Assert.Equal(GateAction.PassAndRecord, OfferGatePolicy.Decide(input, Risky).Action);
        }

        [Test]
        public void TheModsOwnConfirmedCall_PassesAsManual_EvenWhenItWouldOpenTheScreenAgain()
        {
            GateInput input = Manual(ConfirmMode.Always);
            input.Bypass = true;

            GateDecision decision = OfferGatePolicy.Decide(input, Risky);

            Assert.Equal(GateAction.PassAndRecord, decision.Action);
            Assert.Equal(OfferSource.Manual, decision.Source);
        }

        [Test]
        public void TheBypass_WinsOverTheAutomationAndOverTheAi()
        {
            var input = new GateInput { Bypass = true, AutomationRunning = true, IsPlayerCpu = false, Mode = ConfirmMode.Always };

            GateDecision decision = OfferGatePolicy.Decide(input, Risky);

            Assert.Equal(GateAction.PassAndRecord, decision.Action);
            Assert.Equal(OfferSource.Manual, decision.Source);
        }

        [Test]
        public void ACallTheGameWouldIgnore_PassesWithoutAScreenOrALine()
        {
            GateInput already = Manual(ConfirmMode.Always);
            already.AlreadyOffered = true;
            GateInput fails = Manual(ConfirmMode.Always);
            fails.FailsRequirements = true;

            Assert.Equal(GateAction.PassThrough, OfferGatePolicy.Decide(already, Risky).Action, "FR-3 item 7: no screen for an offer the game turns down");
            Assert.Equal(GateAction.PassThrough, OfferGatePolicy.Decide(fails, Risky).Action);
        }

        // ---- the player's offers ----

        [Test]
        public void Always_OpensTheScreen_EvenForASafeOffer()
        {
            GateDecision decision = OfferGatePolicy.Decide(Manual(ConfirmMode.Always), Safe);

            Assert.Equal(GateAction.Review, decision.Action);
            Assert.Equal(OfferSource.Manual, decision.Source);
        }

        [Test]
        public void Risky_OpensTheScreenOnlyWhenARiskIsFound()
        {
            Assert.Equal(GateAction.Review, OfferGatePolicy.Decide(Manual(ConfirmMode.Risky), Risky).Action);

            GateDecision safe = OfferGatePolicy.Decide(Manual(ConfirmMode.Risky), Safe);
            Assert.Equal(GateAction.PassAndRecord, safe.Action);
            Assert.Equal(OfferSource.ManualAutoConfirmed, safe.Source, "AC-4.1: sent, logged as auto-confirmed");
        }

        [Test]
        public void Never_PassesEveryManualOffer_AsAutoConfirmed()
        {
            foreach (Func<RiskFlags> risks in new[] { Safe, Risky })
            {
                GateDecision decision = OfferGatePolicy.Decide(Manual(ConfirmMode.Never), risks);

                Assert.Equal(GateAction.PassAndRecord, decision.Action);
                Assert.Equal(OfferSource.ManualAutoConfirmed, decision.Source, "FR-4 item 3: sent without a screen, and logged all the same");
            }
        }

        [Test]
        public void TheRisks_AreOnlyAskedForInTheModeOnlyRisky_AndOnlyOnce()
        {
            int calls = 0;
            Func<RiskFlags> counting = () => { calls++; return RiskFlags.None; };

            OfferGatePolicy.Decide(Manual(ConfirmMode.Always), counting);
            OfferGatePolicy.Decide(Manual(ConfirmMode.Never), counting);
            Assert.Equal(0, calls, "the analysis costs a call of the game's chance: not for the modes that do not need it");

            OfferGatePolicy.Decide(Manual(ConfirmMode.Risky), counting);
            Assert.Equal(1, calls);

            GateInput ai = Manual(ConfirmMode.Risky);
            ai.IsPlayerCpu = false;
            OfferGatePolicy.Decide(ai, counting);
            GateInput automation = Manual(ConfirmMode.Risky);
            automation.AutomationRunning = true;
            OfferGatePolicy.Decide(automation, counting);
            Assert.Equal(1, calls, "no analysis for the AI and the automation");
        }

        [Test]
        public void ANullRiskFunction_CountsAsNoRisks()
        {
            Assert.Equal(GateAction.PassAndRecord, OfferGatePolicy.Decide(Manual(ConfirmMode.Risky), null).Action);
        }

        [Test]
        public void AModeThatIsNotOneOfTheThree_ActsAsTheDefault_OnlyRisky()
        {
            GateInput input = Manual((ConfirmMode)42);

            Assert.Equal(GateAction.Review, OfferGatePolicy.Decide(input, Risky).Action);
            Assert.Equal(GateAction.PassAndRecord, OfferGatePolicy.Decide(input, Safe).Action);
        }

        [Test]
        public void TheDefaultInput_IsAnAiOffer()
        {
            Assert.Equal(GateAction.PassThrough, OfferGatePolicy.Decide(default(GateInput), Risky).Action);
        }
    }
}
