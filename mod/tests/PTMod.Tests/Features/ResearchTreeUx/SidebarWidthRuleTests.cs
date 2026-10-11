using System;
using PTMod.Features.ResearchTreeUx;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// The arithmetic of the width of the side panel (FR-10, AC-10.1, TSK-021): the factor is held in [1.0; 1.6] (the range of <c>ui.json</c>, itself held in
    /// [1.0; 2.0]), the open panel keeps its right edge where the game put it, and the shut panel hides exactly as far as before, whatever the pivot of the
    /// rectangle: a panel the game hid completely stays completely hidden. The checks are on the edges of the rectangle, not on a copy of the formula.
    /// </summary>
    [Covers(typeof(SidebarWidthRule))]
    public class SidebarWidthRuleTests
    {
        private const float Tolerance = 1e-3f;

        // The positions the game has (SideWindow: visiblePosition -150, hiddenPosition 130) and widths around the likely one.
        private const float Visible = -150f;
        private const float Hidden = 130f;
        private static readonly float[] Widths = { 200f, 260f, 300f, 420f };
        private static readonly float[] Factors = { 1.0f, 1.05f, 1.2f, 1.37f, 1.6f };
        private static readonly float[] Pivots = { 0f, 0.25f, 0.5f, 0.75f, 1f };

        // ---- the range ----

        [Test]
        public void Clamp_KeepsAFactorInsideTheRangeOfTheSpec()
        {
            Assert.Near(1.2, SidebarWidthRule.Clamp(1.2f, 1.0f, 1.6f), 1e-6);
            Assert.Near(1.0, SidebarWidthRule.Clamp(1.0f, 1.0f, 1.6f), 1e-6, "the lower limit itself");
            Assert.Near(1.6, SidebarWidthRule.Clamp(1.6f, 1.0f, 1.6f), 1e-6, "the upper limit itself");
            Assert.Near(1.0, SidebarWidthRule.Clamp(0.4f, 1.0f, 1.6f), 1e-6, "narrower than the game made it");
            Assert.Near(1.0, SidebarWidthRule.Clamp(-3f, 1.0f, 1.6f), 1e-6);
            Assert.Near(1.6, SidebarWidthRule.Clamp(1.61f, 1.0f, 1.6f), 1e-6, "just above the limit");
            Assert.Near(1.6, SidebarWidthRule.Clamp(9f, 1.0f, 1.6f), 1e-6);
        }

        [Test]
        public void Clamp_ANumberThatIsNone_GivesTheOriginalWidth()
        {
            Assert.Near(1.0, SidebarWidthRule.Clamp(float.NaN, 1.0f, 1.6f), 1e-6);
            Assert.Near(1.0, SidebarWidthRule.Clamp(float.PositiveInfinity, 1.0f, 1.6f), 1e-6);
            Assert.Near(1.0, SidebarWidthRule.Clamp(float.NegativeInfinity, 1.0f, 1.6f), 1e-6);
            Assert.Near(1.2, SidebarWidthRule.Clamp(float.NaN, 1.2f, 1.4f), 1e-6, "none is the original width held inside a narrower range");
        }

        [Test]
        public void Clamp_ARangeOfTheFile_IsHeldInsideTheBoundsOfTheDesign()
        {
            Assert.Near(1.2, SidebarWidthRule.Clamp(1.0f, 1.2f, 1.4f), 1e-6, "a file may raise the lower limit");
            Assert.Near(1.4, SidebarWidthRule.Clamp(1.8f, 1.2f, 1.4f), 1e-6);
            Assert.Near(1.0, SidebarWidthRule.Clamp(0.5f, 0.5f, 1.6f), 1e-6, "no file lets the panel be narrower than the game made it");
            Assert.Near(2.0, SidebarWidthRule.Clamp(5f, 1.0f, 5f), 1e-6, "nor above twice its width");
            Assert.Near(1.0, SidebarWidthRule.Clamp(1.3f, float.NaN, 1.0f), 1e-6, "a limit that is none is the default one: 1.0 here");
            Assert.Near(1.6, SidebarWidthRule.Clamp(1.9f, 1.0f, float.NaN), 1e-6, "and 1.6 here");
        }

        [Test]
        public void Clamp_ALimitsPairInTheWrongOrder_IsTakenTheRightWayRound()
        {
            Assert.Near(1.3, SidebarWidthRule.Clamp(1.3f, 1.6f, 1.0f), 1e-6);
            Assert.Near(1.6, SidebarWidthRule.Clamp(1.9f, 1.6f, 1.0f), 1e-6);
            Assert.Near(1.0, SidebarWidthRule.Clamp(0.1f, 1.6f, 1.0f), 1e-6);
        }

        [Test]
        public void Clamp_AFixedWidth_WhenTheLimitsAreEqual()
        {
            Assert.Near(1.3, SidebarWidthRule.Clamp(1.0f, 1.3f, 1.3f), 1e-6);
            Assert.Near(1.3, SidebarWidthRule.Clamp(1.9f, 1.3f, 1.3f), 1e-6);
        }

        // ---- the width ----

        [Test]
        public void Width_IsTheOriginalTimesTheFactor()
        {
            Assert.Near(300, SidebarWidthRule.Width(300f, 1.0f), 1e-4);
            Assert.Near(360, SidebarWidthRule.Width(300f, 1.2f), 1e-3);
            Assert.Near(480, SidebarWidthRule.Width(300f, 1.6f), 1e-3);
            Assert.Near(180, SidebarWidthRule.Growth(300f, 1.6f), 1e-3);
            Assert.Near(0, SidebarWidthRule.Growth(300f, 1.0f), 1e-4);
        }

        [Test]
        public void Width_OfAnOriginalThatIsNotAWidth_IsZero_AndNeverThrows()
        {
            Assert.Equal(0f, SidebarWidthRule.Width(0f, 1.5f));
            Assert.Equal(0f, SidebarWidthRule.Width(-20f, 1.5f));
            Assert.Equal(0f, SidebarWidthRule.Width(float.NaN, 1.5f));
            Assert.Equal(0f, SidebarWidthRule.Width(float.PositiveInfinity, 1.5f));
            Assert.Equal(0f, SidebarWidthRule.Growth(0f, 1.5f));
            Assert.Equal(0f, SidebarWidthRule.Growth(float.NaN, 1.5f));
        }

        [Test]
        public void Width_OfAFactorThatIsNoneOrNegative_IsTheOriginalOrZero()
        {
            Assert.Near(300, SidebarWidthRule.Width(300f, float.NaN), 1e-4, "none is no change");
            Assert.Equal(0f, SidebarWidthRule.Width(300f, -1f), "a negative width does not exist");
        }

        // ---- the places ----

        [Test]
        public void VisiblePosition_KeepsTheRightEdgeWhereTheGamePutIt_WhateverThePivot()
        {
            foreach (float width in Widths)
            foreach (float factor in Factors)
            foreach (float pivot in Pivots)
            {
                float newWidth = SidebarWidthRule.Width(width, factor);
                float position = SidebarWidthRule.VisiblePosition(Visible, width, factor, pivot);

                Assert.Near(SidebarWidthRule.RightEdge(Visible, width, pivot), SidebarWidthRule.RightEdge(position, newWidth, pivot), Tolerance,
                    "right edge, width " + width + ", factor " + factor + ", pivot " + pivot);
            }
        }

        [Test]
        public void VisiblePosition_TheLeftEdgeGoesLeftByTheGrowth_SoTheEdgeFollowsTheHand()
        {
            foreach (float width in Widths)
            foreach (float factor in Factors)
            foreach (float pivot in Pivots)
            {
                float newWidth = SidebarWidthRule.Width(width, factor);
                float position = SidebarWidthRule.VisiblePosition(Visible, width, factor, pivot);

                float before = SidebarWidthRule.LeftEdge(Visible, width, pivot);
                float after = SidebarWidthRule.LeftEdge(position, newWidth, pivot);
                Assert.Near(before - SidebarWidthRule.Growth(width, factor), after, Tolerance, "left edge, width " + width + ", factor " + factor + ", pivot " + pivot);
            }
        }

        [Test]
        public void HiddenPosition_HidesTheShutPanelAsFarAsBefore_WhateverThePivot()
        {
            foreach (float width in Widths)
            foreach (float factor in Factors)
            foreach (float pivot in Pivots)
            {
                float newWidth = SidebarWidthRule.Width(width, factor);
                float position = SidebarWidthRule.HiddenPosition(Hidden, width, factor, pivot);

                Assert.Near(SidebarWidthRule.LeftEdge(Hidden, width, pivot), SidebarWidthRule.LeftEdge(position, newWidth, pivot), Tolerance,
                    "left edge of the shut panel, width " + width + ", factor " + factor + ", pivot " + pivot);
            }
        }

        [Test]
        public void HiddenPosition_APanelTheGameHidCompletely_StaysCompletelyOutOfTheScreen()
        {
            // The screen ends at 0 (the right edge of the open panel). The game hid this panel exactly: its left edge stands at the screen edge.
            const float width = 260f;
            foreach (float pivot in Pivots)
            {
                float visible = -SidebarWidthRule.RightEdge(0f, width, pivot);   // the right edge of the open panel is at 0
                float hidden = visible + width;                                  // slid right by its own width
                Assert.Near(0, SidebarWidthRule.LeftEdge(hidden, width, pivot), Tolerance, "the setup: shut exactly at the screen edge, pivot " + pivot);

                foreach (float factor in Factors)
                {
                    float newWidth = SidebarWidthRule.Width(width, factor);
                    float newVisible = SidebarWidthRule.VisiblePosition(visible, width, factor, pivot);
                    float newHidden = SidebarWidthRule.HiddenPosition(hidden, width, factor, pivot);

                    Assert.Near(0, SidebarWidthRule.RightEdge(newVisible, newWidth, pivot), Tolerance, "open: the right edge on the screen edge, factor " + factor);
                    Assert.True(SidebarWidthRule.LeftEdge(newHidden, newWidth, pivot) >= -Tolerance,
                        "shut: no part of the panel on the screen, factor " + factor + ", pivot " + pivot);
                }
            }
        }

        [Test]
        public void TheTravelOfTheGame_GrowsByTheGrowthOfTheWidth()
        {
            foreach (float width in Widths)
            foreach (float factor in Factors)
            foreach (float pivot in Pivots)
            {
                float travel = (Hidden - Visible) + SidebarWidthRule.Growth(width, factor);
                float actual = SidebarWidthRule.HiddenPosition(Hidden, width, factor, pivot) - SidebarWidthRule.VisiblePosition(Visible, width, factor, pivot);
                Assert.Near(travel, actual, Tolerance, "width " + width + ", factor " + factor + ", pivot " + pivot);
            }
        }

        [Test]
        public void TheOriginalWidth_MovesNothing()
        {
            foreach (float pivot in Pivots)
            {
                Assert.Near(Visible, SidebarWidthRule.VisiblePosition(Visible, 300f, 1.0f, pivot), 1e-4);
                Assert.Near(Hidden, SidebarWidthRule.HiddenPosition(Hidden, 300f, 1.0f, pivot), 1e-4);
            }
        }

        [Test]
        public void ThePositionsWithoutAPivot_AreThoseOfAPivotOnTheRightEdge()
        {
            // The form of the design: the position names the right edge, so the open place stays and the shut place moves by the whole growth.
            Assert.Near(Visible, SidebarWidthRule.VisiblePosition(Visible, 300f, 1.6f), 1e-4);
            Assert.Near(Hidden + 180f, SidebarWidthRule.HiddenPosition(Hidden, 300f, 1.6f), 1e-3);
            Assert.Near(SidebarWidthRule.HiddenPosition(Hidden, 300f, 1.3f, 1f), SidebarWidthRule.HiddenPosition(Hidden, 300f, 1.3f), 1e-6);
            Assert.Near(SidebarWidthRule.VisiblePosition(Visible, 300f, 1.3f, 1f), SidebarWidthRule.VisiblePosition(Visible, 300f, 1.3f), 1e-6);
        }

        [Test]
        public void ThePositions_OfAnOriginalThatIsNotAWidth_AreTheOriginalPositions()
        {
            Assert.Near(Visible, SidebarWidthRule.VisiblePosition(Visible, 0f, 1.6f, 0.5f), 1e-6);
            Assert.Near(Hidden, SidebarWidthRule.HiddenPosition(Hidden, float.NaN, 1.6f, 0.5f), 1e-6);
            Assert.Near(Hidden, SidebarWidthRule.HiddenPosition(Hidden, -4f, 1.6f, 0.5f), 1e-6);
        }

        [Test]
        public void APivotOutOfRange_IsHeldInsideIt_AndOneThatIsNone_IsTheRightEdge()
        {
            Assert.Near(SidebarWidthRule.HiddenPosition(Hidden, 300f, 1.6f, 1f), SidebarWidthRule.HiddenPosition(Hidden, 300f, 1.6f, 7f), 1e-6);
            Assert.Near(SidebarWidthRule.HiddenPosition(Hidden, 300f, 1.6f, 0f), SidebarWidthRule.HiddenPosition(Hidden, 300f, 1.6f, -2f), 1e-6);
            Assert.Near(SidebarWidthRule.HiddenPosition(Hidden, 300f, 1.6f, 1f), SidebarWidthRule.HiddenPosition(Hidden, 300f, 1.6f, float.NaN), 1e-6);
        }

        // ---- the edges ----

        [Test]
        public void TheEdges_AreTheRectangleAroundThePivot()
        {
            Assert.Near(100, SidebarWidthRule.LeftEdge(100f, 50f, 0f), 1e-6);
            Assert.Near(50, SidebarWidthRule.LeftEdge(100f, 50f, 1f), 1e-6);
            Assert.Near(75, SidebarWidthRule.LeftEdge(100f, 50f, 0.5f), 1e-6);
            Assert.Near(150, SidebarWidthRule.RightEdge(100f, 50f, 0f), 1e-6);
            Assert.Near(100, SidebarWidthRule.RightEdge(100f, 50f, 1f), 1e-6);
            Assert.Near(125, SidebarWidthRule.RightEdge(100f, 50f, 0.5f), 1e-6);
        }

        // ---- the drag ----

        [Test]
        public void FactorAfterDrag_ADragToTheLeft_WidensThePanel_ByTheDistanceOverTheOriginalWidth()
        {
            Assert.Near(1.4, SidebarWidthRule.FactorAfterDrag(1.0f, 300f, 120f, 1.0f, 1.6f), 1e-5, "120 px on a panel 300 wide");
            Assert.Near(1.2, SidebarWidthRule.FactorAfterDrag(1.0f, 300f, 60f, 1.0f, 1.6f), 1e-5);
            Assert.Near(1.5, SidebarWidthRule.FactorAfterDrag(1.2f, 300f, 90f, 1.0f, 1.6f), 1e-5, "from the factor it already had");
        }

        [Test]
        public void FactorAfterDrag_AC10_1_ADragOf120px_ReachesTheLimitOnAPanelOf200_AndStopsThereBeyondIt()
        {
            Assert.Near(1.6, SidebarWidthRule.FactorAfterDrag(1.0f, 200f, 120f, 1.0f, 1.6f), 1e-5);
            Assert.Near(1.6, SidebarWidthRule.FactorAfterDrag(1.0f, 200f, 600f, 1.0f, 1.6f), 1e-5, "past the limit the factor stays at the limit");
            Assert.Near(1.6, SidebarWidthRule.FactorAfterDrag(1.0f, 150f, 120f, 1.0f, 1.6f), 1e-5, "a narrower panel reaches the limit sooner");
        }

        [Test]
        public void FactorAfterDrag_ADragToTheRight_NarrowsThePanel_ButNotBelowTheOriginalWidth()
        {
            Assert.Near(1.2, SidebarWidthRule.FactorAfterDrag(1.5f, 300f, -90f, 1.0f, 1.6f), 1e-5);
            Assert.Near(1.0, SidebarWidthRule.FactorAfterDrag(1.5f, 300f, -900f, 1.0f, 1.6f), 1e-5, "the original width is the narrowest");
            Assert.Near(1.0, SidebarWidthRule.FactorAfterDrag(1.0f, 300f, -10f, 1.0f, 1.6f), 1e-5);
        }

        [Test]
        public void FactorAfterDrag_TheLeftEdgeFollowsTheHand_UpToTheLimit()
        {
            foreach (float pivot in Pivots)
            foreach (float drag in new[] { 0f, 15f, 60f, 100f, 140f })
            {
                const float width = 300f;
                float factor = SidebarWidthRule.FactorAfterDrag(1.0f, width, drag, 1.0f, 1.6f);
                float position = SidebarWidthRule.VisiblePosition(Visible, width, factor, pivot);
                float edge = SidebarWidthRule.LeftEdge(position, SidebarWidthRule.Width(width, factor), pivot);

                Assert.Near(SidebarWidthRule.LeftEdge(Visible, width, pivot) - drag, edge, Tolerance, "drag " + drag + ", pivot " + pivot);
            }
        }

        [Test]
        public void FactorAfterDrag_WithNothingToGoOn_KeepsTheStartingFactor()
        {
            Assert.Near(1.3, SidebarWidthRule.FactorAfterDrag(1.3f, 0f, 50f, 1.0f, 1.6f), 1e-6, "no width");
            Assert.Near(1.3, SidebarWidthRule.FactorAfterDrag(1.3f, -5f, 50f, 1.0f, 1.6f), 1e-6);
            Assert.Near(1.3, SidebarWidthRule.FactorAfterDrag(1.3f, float.NaN, 50f, 1.0f, 1.6f), 1e-6);
            Assert.Near(1.3, SidebarWidthRule.FactorAfterDrag(1.3f, 300f, float.NaN, 1.0f, 1.6f), 1e-6, "no distance");
            Assert.Near(1.0, SidebarWidthRule.FactorAfterDrag(float.NaN, 300f, 0f, 1.0f, 1.6f), 1e-6, "no start is the original width");
            Assert.Near(1.6, SidebarWidthRule.FactorAfterDrag(2.5f, 300f, 0f, 1.0f, 1.6f), 1e-6, "a start above the limit is held at it");
        }

        // ---- neutral and open ----

        [Test]
        public void IsNeutral_TheOriginalWidth_AndNumbersThatAreNone()
        {
            Assert.True(SidebarWidthRule.IsNeutral(1.0f));
            Assert.True(SidebarWidthRule.IsNeutral(1.0005f));
            Assert.True(SidebarWidthRule.IsNeutral(0.9995f));
            Assert.False(SidebarWidthRule.IsNeutral(1.002f));
            Assert.False(SidebarWidthRule.IsNeutral(1.6f));
            Assert.True(SidebarWidthRule.IsNeutral(float.NaN));
            Assert.True(SidebarWidthRule.IsNeutral(float.PositiveInfinity));
        }

        [Test]
        public void LooksOpen_TheNearerOfTheTwoPositionsDecides_AndATieIsOpen()
        {
            Assert.True(SidebarWidthRule.LooksOpen(-150f, -150f, 130f));
            Assert.True(SidebarWidthRule.LooksOpen(-100f, -150f, 130f));
            Assert.False(SidebarWidthRule.LooksOpen(100f, -150f, 130f));
            Assert.False(SidebarWidthRule.LooksOpen(130f, -150f, 130f));
            Assert.True(SidebarWidthRule.LooksOpen(-10f, -150f, 130f), "exactly between the two: open");
        }

        [Test]
        public void TheLimitsOfTheDesign_AreTheOnesOfTheSpec()
        {
            Assert.Equal(1.0f, SidebarWidthRule.DefaultMin);
            Assert.Equal(1.6f, SidebarWidthRule.DefaultMax);
            Assert.Equal(1.0f, SidebarWidthRule.AbsoluteMin);
            Assert.Equal(2.0f, SidebarWidthRule.AbsoluteMax);
            Assert.Equal(1.0f, SidebarWidthRule.NeutralFactor);
            Assert.Equal(UxPrefsModel.WidthFactorMin, SidebarWidthRule.AbsoluteMin, "the profile and the rule hold the same bounds");
            Assert.Equal(UxPrefsModel.WidthFactorMax, SidebarWidthRule.AbsoluteMax);
        }
    }
}
