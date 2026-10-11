using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The rules of the shared lab (D2; FR-2 items 1 and 5, FR-3 item 5, FR-7 items 1-2; TSK-004): the confirmation only for the player and only over a
    /// technology, the AI does not interrupt a technology with 30 days or fewer left (AC-7.3), a conflict pauses the player's architecture and stops the
    /// AI's technology, the funding of the AI by difficulty (0.6–1.6) and the limit of the player's slider (1.0 / 5.0).
    /// </summary>
    [Covers(typeof(LabRules), typeof(LabStatus), typeof(ConflictResolution))]
    public class LabRulesTests
    {
        [Test]
        public void Status_OfEveryCombination()
        {
            Assert.Equal(LabStatus.Free, LabRules.Status(false, false, false));
            Assert.Equal(LabStatus.Free, LabRules.Status(false, false, true), "a pause without an architecture means nothing");
            Assert.Equal(LabStatus.Technology, LabRules.Status(true, false, false));
            Assert.Equal(LabStatus.Architecture, LabRules.Status(false, true, false));
            Assert.Equal(LabStatus.ArchitecturePaused, LabRules.Status(false, true, true));
            Assert.Equal(LabStatus.Technology, LabRules.Status(true, true, true), "a paused architecture and a chosen technology: the lab is busy with the technology");
            Assert.Equal(LabStatus.Technology, LabRules.Status(true, true, false), "a conflict shows the technology until the arbiter settles it");
        }

        [Test]
        public void IsOccupiedByArchitecture_OnlyAnActiveOne()
        {
            Assert.True(LabRules.IsOccupiedByArchitecture(true, false));
            Assert.False(LabRules.IsOccupiedByArchitecture(true, true), "paused: the AI may choose a technology again (FR-7 item 4)");
            Assert.False(LabRules.IsOccupiedByArchitecture(false, false));
        }

        [Test]
        public void Confirmation_OnlyThePlayer_OnlyOverATechnology()
        {
            Assert.True(LabRules.NeedsConfirmation(true, true), "FR-2 item 1");
            Assert.False(LabRules.NeedsConfirmation(true, false), "a free lab: no question (edge case of the requirements)");
            Assert.False(LabRules.NeedsConfirmation(false, true), "an AI company interrupts without asking");
            Assert.False(LabRules.NeedsConfirmation(false, false));
        }

        [Test]
        public void Ai_DoesNotInterruptANearlyFinishedTechnology()
        {
            const int noInterrupt = 30;   // ai.noInterruptTimeLeftDays of balance.json
            Assert.Equal(noInterrupt, BalanceConfig.Defaults().ai.noInterruptTimeLeftDays);

            Assert.False(LabRules.CanInterruptForAi(20, noInterrupt), "AC-7.3: 20 days left - the decision waits for the next month");
            Assert.False(LabRules.CanInterruptForAi(30, noInterrupt), "30 days left is not more than 30");
            Assert.True(LabRules.CanInterruptForAi(31, noInterrupt));
            Assert.True(LabRules.CanInterruptForAi(400, noInterrupt));
            Assert.True(LabRules.CanInterruptForAi(-1, noInterrupt), "a free lab can always be taken");
            Assert.True(LabRules.CanInterruptForAi(1, -5), "a negative limit counts as 0");
            Assert.False(LabRules.CanInterruptForAi(0, -5), "a technology finishing today is never interrupted");
            Assert.False(LabRules.CanInterruptForAi(0, 0));
        }

        [Test]
        public void Conflict_PlayerPausesTheArchitecture_AiStopsTheTechnology()
        {
            Assert.Equal(ConflictResolution.PauseArchitecture, LabRules.ResolveConflict(true, true, true), "AC-2.4: the last choice wins");
            Assert.Equal(ConflictResolution.StopTechnology, LabRules.ResolveConflict(false, true, true), "the AI keeps its architecture");
            Assert.Equal(ConflictResolution.None, LabRules.ResolveConflict(true, false, true), "a paused architecture is no conflict");
            Assert.Equal(ConflictResolution.None, LabRules.ResolveConflict(false, true, false), "an empty slot is no conflict");
            Assert.Equal(ConflictResolution.None, LabRules.ResolveConflict(true, false, false));
        }

        [Test]
        public void AiFunding_ByDifficulty()
        {
            float[] table = BalanceConfig.Defaults().ai.fundingByDifficulty;
            float[] expected = { 0.6f, 0.8f, 1.0f, 1.3f, 1.6f };
            for (int level = 0; level < expected.Length; level++)
            {
                Assert.Near(expected[level], LabRules.AiFunding(level, table), 1e-6, "difficulty " + level);
            }

            Assert.Near(0.6, LabRules.AiFunding(-3, table), 1e-6, "below the table: the first entry");
            Assert.Near(1.6, LabRules.AiFunding(9, table), 1e-6, "above the table: the last entry");
            Assert.Equal(LabRules.DefaultAiFunding, LabRules.AiFunding(2, null));
            Assert.Equal(LabRules.DefaultAiFunding, LabRules.AiFunding(2, new float[0]));
            Assert.Equal(LabRules.DefaultAiFunding, LabRules.AiFunding(0, new[] { float.NaN }));
            Assert.Equal(LabRules.DefaultAiFunding, LabRules.AiFunding(0, new[] { -1f }));
            Assert.Equal(LabRules.InnovationFundingLimit, LabRules.AiFunding(0, new[] { 50f }), "never above 500 %");
        }

        [Test]
        public void PlayerSlider_OneOrFive()
        {
            Assert.Equal(1f, LabRules.PlayerFundingLimit(false), "1–100 %");
            Assert.Equal(5f, LabRules.PlayerFundingLimit(true), "up to 500 % with the innovation effort");

            Assert.Equal(1f, LabRules.ClampPlayerFunding(3f, false));
            Assert.Equal(3f, LabRules.ClampPlayerFunding(3f, true));
            Assert.Equal(5f, LabRules.ClampPlayerFunding(9f, true));
            Assert.Equal(LabRules.MinFunding, LabRules.ClampPlayerFunding(0f, false), "the slider starts at 1 %");
            Assert.Equal(LabRules.MinFunding, LabRules.ClampPlayerFunding(-2f, true));
            Assert.Equal(1f, LabRules.ClampPlayerFunding(float.NaN, false));
            Assert.Near(0.42, LabRules.ClampPlayerFunding(0.42f, false), 1e-6);
        }
    }
}
