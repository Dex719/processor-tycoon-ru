using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.PressReviews;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The adjusters of other features on the draft of a review (FR-11 items 3 and 5, TSK-016): what they may change, how far (an axis ±30, the total ±20,
    /// per adjuster and in all), what happens to an adjuster that throws, and that the review of a CPU is the same without them and 15 points lower with an
    /// adjuster of -15 (AC-11.1).
    /// </summary>
    [Covers(typeof(AdjusterPipeline), typeof(AdjusterSet), typeof(AdjusterStep), typeof(AdjusterReport))]
    public class AdjusterPipelineTests
    {
        private static readonly DateTime Day = new DateTime(1995, 3, 15);
        private static readonly float[] MidWeights = { 0.35f, 0.35f, 0.15f, 0.15f };

        private static ReviewDraft Draft(float performance = 70f, float value = 60f, float efficiency = 50f, float thermal = 40f)
        {
            var draft = new ReviewDraft { AxisScores = new[] { performance, value, efficiency, thermal } };
            draft.BaseScore = ReviewMath.WeightedScore(draft.AxisScores, MidWeights);
            draft.CappedScore = ReviewMath.ApplyCap(draft.BaseScore, draft.AxisScores, MidWeights, new CapConfig());
            return draft;
        }

        private static AdjusterStep Step(string id, Action<ReviewDraft> run)
        {
            return new AdjusterStep { Id = id, Run = run };
        }

        private static AdjusterSet Set(params AdjusterStep[] steps)
        {
            return new AdjusterSet { Steps = steps };
        }

        private static AdjusterReport Apply(ReviewDraft draft, AdjusterSet set, float[] weights = null)
        {
            return AdjusterPipeline.Apply(draft, set, new AdjusterLimits(), weights, new CapConfig());
        }

        // ---- nothing to do ----

        [Test]
        public void WithoutAdjusters_TheDraftStaysAsItIs()
        {
            ReviewDraft draft = Draft();
            AdjusterReport none = Apply(draft, null);
            AdjusterReport empty = Apply(draft, Set());

            Assert.Equal(new[] { 70f, 60f, 50f, 40f }, draft.AxisScores);
            Assert.Equal(0f, draft.OverallAdjustment);
            Assert.Equal(0, draft.Notes.Count);
            Assert.False(none.Changed || empty.Changed);
            Assert.Equal(0, none.Applied + empty.Applied);
        }

        [Test]
        public void ANullDraft_IsABugOfTheCaller()
        {
            Assert.Throws<ArgumentNullException>(() => AdjusterPipeline.Apply(null, Set(), new AdjusterLimits(), null, new CapConfig()));
        }

        // ---- what an adjuster may change ----

        [Test]
        public void ACorrectionOfTheTotal_LowersItByThatMuch_AC111()
        {
            ReviewDraft draft = Draft();
            AdjusterReport report = Apply(draft, Set(Step("design-flaw-recalls", d =>
            {
                d.OverallAdjustment -= 15f;
                d.Notes.Add("press.note.knownFlaw");
            })));

            Assert.Equal(-15f, draft.OverallAdjustment);
            Assert.Equal(new List<string> { "press.note.knownFlaw" }, draft.Notes);
            Assert.Equal(1, report.Applied);
            Assert.Equal(0, report.Failed);
            Assert.Equal(0, report.Clipped);
            Assert.True(report.Changed);
        }

        [Test]
        public void ACorrectionOfAnAxis_MovesTheAxis_AndWorksTheTotalAgain()
        {
            ReviewDraft draft = Draft();
            Apply(draft, Set(Step("a", d => d.AxisScores[(int)Axis.Performance] -= 20f)), MidWeights);

            Assert.Near(50.0, draft.AxisScores[(int)Axis.Performance], 1e-4);
            Assert.Near(60.0, draft.AxisScores[(int)Axis.Value], 1e-4, "the other axes stay");
            float expected = ReviewMath.WeightedScore(draft.AxisScores, MidWeights);
            Assert.Near(expected, draft.BaseScore, 1e-4, "the weighted total follows the axes");
            Assert.Near(ReviewMath.ApplyCap(expected, draft.AxisScores, MidWeights, new CapConfig()), draft.CappedScore, 1e-4, "and so does the cap by the weakest axis");
        }

        [Test]
        public void WithoutWeights_TheTotalsAreLeftAlone()
        {
            ReviewDraft draft = Draft();
            float baseScore = draft.BaseScore;
            Apply(draft, Set(Step("a", d => d.AxisScores[0] = 10f)), null);

            Assert.Equal(baseScore, draft.BaseScore);
        }

        // ---- the limits ----

        [Test]
        public void AnAxisMovesByAtMost30Points_AndTheCutIsCounted()
        {
            ReviewDraft up = Draft(performance: 40f);
            AdjusterReport upReport = Apply(up, Set(Step("a", d => d.AxisScores[0] += 50f)));
            ReviewDraft down = Draft(performance: 80f);
            Apply(down, Set(Step("a", d => d.AxisScores[0] -= 60f)));

            Assert.Equal(70f, up.AxisScores[0], "+50 is cut to +30");
            Assert.Equal(50f, down.AxisScores[0], "-60 is cut to -30");
            Assert.Equal(1, upReport.Clipped);
        }

        [Test]
        public void TheTotalMovesByAtMost20Points()
        {
            ReviewDraft low = Draft();
            ReviewDraft high = Draft();
            Apply(low, Set(Step("a", d => d.OverallAdjustment = -45f)));
            Apply(high, Set(Step("a", d => d.OverallAdjustment = 33f)));

            Assert.Equal(-20f, low.OverallAdjustment);
            Assert.Equal(20f, high.OverallAdjustment);
        }

        [Test]
        public void TwoAdjusters_TogetherStayWithinTheLimits()
        {
            ReviewDraft draft = Draft(performance: 50f);
            AdjusterReport report = Apply(draft, Set(
                Step("a", d => { d.AxisScores[0] += 25f; d.OverallAdjustment += 15f; }),
                Step("b", d => { d.AxisScores[0] += 25f; d.OverallAdjustment += 15f; })));

            Assert.Equal(80f, draft.AxisScores[0], "50 + 25 + 25 would be 100: at most 30 from the computed 50 in all");
            Assert.Equal(20f, draft.OverallAdjustment, "15 + 15 would be 30: at most 20 in all");
            Assert.True(report.Clipped >= 2);
            Assert.Equal(2, report.Applied);
        }

        [Test]
        public void OneAdjusterCannotTakeBackWhatAnotherGave_MoreThanItsOwnLimit()
        {
            ReviewDraft draft = Draft(performance: 50f);
            Apply(draft, Set(
                Step("a", d => d.AxisScores[0] += 30f),
                Step("b", d => d.AxisScores[0] -= 100f)));

            Assert.Equal(50f, draft.AxisScores[0], "+30 and then at most -30 again");
        }

        [Test]
        public void AnAxisStaysOnTheScaleOf0To100()
        {
            ReviewDraft top = Draft(performance: 90f);
            ReviewDraft bottom = Draft(thermal: 10f);
            Apply(top, Set(Step("a", d => d.AxisScores[0] += 30f)));
            Apply(bottom, Set(Step("a", d => d.AxisScores[3] -= 30f)));

            Assert.Equal(100f, top.AxisScores[0]);
            Assert.Equal(0f, bottom.AxisScores[3]);
        }

        [Test]
        public void TheLimitsAreTheConfigurations()
        {
            ReviewDraft draft = Draft(performance: 50f);
            var limits = new AdjusterLimits { Axis = 5f, Overall = 2f };
            AdjusterPipeline.Apply(draft, Set(Step("a", d => { d.AxisScores[0] += 20f; d.OverallAdjustment += 20f; })), limits, null, new CapConfig());

            Assert.Equal(55f, draft.AxisScores[0]);
            Assert.Equal(2f, draft.OverallAdjustment);
        }

        [Test]
        public void ZeroLimits_MeanNoCorrectionAtAll()
        {
            ReviewDraft draft = Draft();
            AdjusterPipeline.Apply(draft, Set(Step("a", d => { d.AxisScores[0] = 99f; d.OverallAdjustment = 9f; })), new AdjusterLimits { Axis = 0f, Overall = 0f }, null, new CapConfig());

            Assert.Equal(70f, draft.AxisScores[0]);
            Assert.Equal(0f, draft.OverallAdjustment);
        }

        [Test]
        public void ANumberThatIsNotAnumber_CountsAsNoChange()
        {
            ReviewDraft draft = Draft();
            Apply(draft, Set(Step("a", d =>
            {
                d.AxisScores[0] = float.NaN;
                d.AxisScores[1] = float.PositiveInfinity;
                d.OverallAdjustment = float.NaN;
            })));

            Assert.Equal(new[] { 70f, 60f, 50f, 40f }, draft.AxisScores);
            Assert.Equal(0f, draft.OverallAdjustment);
        }

        [Test]
        public void AnAdjusterThatReplacesTheArrays_CannotHarmTheDraft()
        {
            ReviewDraft draft = Draft();
            Apply(draft, Set(
                Step("null", d => { d.AxisScores = null; d.Notes = null; }),
                Step("short", d => d.AxisScores = new[] { 11f })));

            Assert.Equal(4, draft.AxisScores.Length);
            Assert.NotNull(draft.Notes);
            Assert.Equal(40f, draft.AxisScores[0], "the one number the short array has is read: 11 asks for -59, the limit gives -30");
            Assert.Equal(60f, draft.AxisScores[1], "the axes the array lacks stay");
        }

        // ---- the notes ----

        [Test]
        public void NotesAreKept_OnceEach_InTheOrderTheyCame()
        {
            ReviewDraft draft = Draft();
            draft.Notes.Add("press.review.noPrice");
            Apply(draft, Set(
                Step("a", d => { d.Notes.Add("press.note.knownFlaw"); d.Notes.Add("press.note.knownFlaw"); }),
                Step("b", d => d.Notes.Add("press.note.flawPublic"))));

            Assert.Equal(new List<string> { "press.review.noPrice", "press.note.knownFlaw", "press.note.flawPublic" }, draft.Notes);
        }

        [Test]
        public void ANoteThatIsEmptyTooLongOrNotInTheTables_IsDropped()
        {
            ReviewDraft draft = Draft();
            var set = Set(Step("a", d =>
            {
                d.Notes.Add(null);
                d.Notes.Add("  ");
                d.Notes.Add(new string('x', AdjusterPipeline.MaxNoteLength + 1));
                d.Notes.Add("press.note.unknown");
                d.Notes.Add("press.note.knownFlaw");
            }));
            set.IsNoteKnown = key => key == "press.note.knownFlaw";
            Apply(draft, set);

            Assert.Equal(new List<string> { "press.note.knownFlaw" }, draft.Notes);
        }

        [Test]
        public void ACheckOfNotesThatThrows_DoesNotCostTheReviewItsNote()
        {
            ReviewDraft draft = Draft();
            var set = Set(Step("a", d => d.Notes.Add("press.note.knownFlaw")));
            set.IsNoteKnown = key => { throw new InvalidOperationException("tables are not there"); };
            Apply(draft, set);

            Assert.Equal(new List<string> { "press.note.knownFlaw" }, draft.Notes);
        }

        [Test]
        public void AReviewCarriesAtMostSixNotes()
        {
            ReviewDraft draft = Draft();
            Apply(draft, Set(Step("a", d =>
            {
                for (int i = 0; i < 20; i++) d.Notes.Add("press.note.n" + i);
            })));

            Assert.Equal(AdjusterPipeline.MaxNotes, draft.Notes.Count);
            Assert.Equal("press.note.n0", draft.Notes[0]);
        }

        [Test]
        public void AnAdjusterCannotTakeANoteAway()
        {
            ReviewDraft draft = Draft();
            draft.Notes.Add("press.review.noPrice");
            Apply(draft, Set(Step("a", d => d.Notes.Clear())));

            Assert.Equal(new List<string> { "press.review.noPrice" }, draft.Notes);
        }

        // ---- order ----

        [Test]
        public void EachAdjusterSeesTheDraftAsTheEarlierOnesLeftIt()
        {
            ReviewDraft draft = Draft(performance: 50f);
            float seenByB = -1f;
            Apply(draft, Set(
                Step("a", d => d.AxisScores[0] += 10f),
                Step("b", d => seenByB = d.AxisScores[0])));

            Assert.Equal(60f, seenByB);
        }

        [Test]
        public void TheAdjustersRunInTheGivenOrder()
        {
            var order = new List<string>();
            Apply(Draft(), Set(Step("first", d => order.Add("first")), Step("second", d => order.Add("second")), Step("third", d => order.Add("third"))));

            Assert.Equal(new List<string> { "first", "second", "third" }, order);
        }

        [Test]
        public void ANullStepOrOneWithoutACode_IsSkipped()
        {
            ReviewDraft draft = Draft();
            AdjusterReport report = Apply(draft, new AdjusterSet { Steps = new[] { null, new AdjusterStep { Id = "empty" }, Step("a", d => d.OverallAdjustment = 3f) } });

            Assert.Equal(3f, draft.OverallAdjustment);
            Assert.Equal(1, report.Applied);
        }

        // ---- an adjuster that throws (FR-11 item 5) ----

        [Test]
        public void AnAdjusterThatThrows_ChangesNothing_AndTheNextOneStillRuns()
        {
            ReviewDraft draft = Draft();
            var errors = new List<string>();
            var set = Set(
                Step("broken", d =>
                {
                    d.AxisScores[0] += 20f;     // done before it fails: this must not count
                    d.OverallAdjustment = -10f;
                    d.Notes.Add("press.note.broken");
                    throw new InvalidOperationException("boom");
                }),
                Step("fine", d => d.OverallAdjustment -= 5f));
            set.OnError = (id, ex) => errors.Add(id + ": " + ex.Message);

            AdjusterReport report = Apply(draft, set);

            Assert.Equal(70f, draft.AxisScores[0], "the half-done change of the broken adjuster is dropped");
            Assert.Equal(-5f, draft.OverallAdjustment, "only the second adjuster's correction counts");
            Assert.Equal(0, draft.Notes.Count);
            Assert.Equal(new List<string> { "broken: boom" }, errors, "the error is reported with the id of the adjuster");
            Assert.Equal(1, report.Failed);
            Assert.Equal(new List<string> { "broken" }, report.FailedIds);
            Assert.Equal(1, report.Applied);
        }

        [Test]
        public void AFailureOfTheErrorHandler_IsNotASecondFailure()
        {
            ReviewDraft draft = Draft();
            var set = Set(Step("broken", d => { throw new InvalidOperationException("boom"); }), Step("fine", d => d.OverallAdjustment = 4f));
            set.OnError = (id, ex) => { throw new InvalidOperationException("the log is broken too"); };

            Apply(draft, set);

            Assert.Equal(4f, draft.OverallAdjustment);
        }

        [Test]
        public void WithoutAnErrorHandler_AFailureIsOnlyCounted()
        {
            AdjusterReport report = Apply(Draft(), Set(Step("broken", d => { throw new InvalidOperationException(); })));

            Assert.Equal(1, report.Failed);
            Assert.False(report.Changed);
        }

        // ---- inside the review of a CPU (AC-11.1) ----

        private sealed class QuietRandom : IRandomSource
        {
            public float NextGaussian()
            {
                return 0f;
            }

            public int Range(int minInclusive, int maxExclusive)
            {
                return minInclusive;
            }
        }

        private static ReviewInput Input()
        {
            var peer = new CpuSnapshot
            {
                SaveId = 100, CompanyKey = "ai:1", Name = "CPU 100", Ips = 100f, Price = 100f, PowerConsumption = 10f, Temperature = 70f,
                SafeTemperature = 90f, ReleaseYearFraction = 1995.2f, Segment = Segment.HighEnd,
            };
            var cpu = new CpuSnapshot
            {
                SaveId = 7, CompanyKey = "player", Name = "CPU 7", Ips = 110f, Price = 110f, PowerConsumption = 110f / 9f, Temperature = 70f,
                SafeTemperature = 90f, ReleaseYearFraction = 1995.2f, Segment = Segment.HighEnd,
            };
            return new ReviewInput { Cpu = cpu, Peers = new[] { peer }, NowYearFraction = 1995.2f, Difficulty = 2 };
        }

        private static ReviewRecord Compose(AdjusterSet adjusters)
        {
            var pending = new PendingReview { cpuSaveId = 7, companyKey = "player", releasedOn = "1995-03-01", publishOn = "1995-03-15" };
            return ReviewComposer.Compose(pending, "Intelligent", Input(), Day, PressContentFixture.Outlets(), PressContentFixture.Config(), new QuietRandom(), adjusters);
        }

        [Test]
        public void TheMetascoreIs15LowerWithAnAdjusterOfMinus15_AndTheCardGetsTheNote_AC111()
        {
            ReviewRecord plain = Compose(null);
            ReviewRecord adjusted = Compose(Set(Step("design-flaw-recalls", d =>
            {
                d.OverallAdjustment -= 15f;
                d.Notes.Add("press.note.knownFlaw");
            })));

            Assert.Equal(plain.metascore - 15, adjusted.metascore);
            for (int i = 0; i < plain.outlets.Count; i++) Assert.Equal(plain.outlets[i].score - 15, adjusted.outlets[i].score, "every outlet marks 15 lower: " + plain.outlets[i].outletId);
            Assert.Equal(new List<string> { "press.note.knownFlaw" }, adjusted.notes);
            Assert.Equal(0, plain.notes.Count);
        }

        [Test]
        public void AnAxisCorrectionChangesTheAxesOfTheRecord_AndTheMarks()
        {
            ReviewRecord plain = Compose(null);
            ReviewRecord adjusted = Compose(Set(Step("a", d => d.AxisScores[(int)Axis.Performance] += 10f)));

            Assert.Near(plain.axes[(int)Axis.Performance] + 10f, adjusted.axes[(int)Axis.Performance], 1e-4);
            Assert.True(adjusted.metascore > plain.metascore, "a better performance axis gives a better mark");
        }

        [Test]
        public void AnAdjusterThatThrows_DoesNotStopTheReview_TheRecordIsTheSame()
        {
            ReviewRecord plain = Compose(null);
            var set = Set(Step("broken", d =>
            {
                d.OverallAdjustment = -20f;
                throw new InvalidOperationException("boom");
            }));
            ReviewRecord record = Compose(set);

            Assert.Equal(plain.metascore, record.metascore);
            Assert.Equal(plain.axes, record.axes);
            Assert.Equal(plain.verdictKey, record.verdictKey);
        }

        [Test]
        public void TheNoPriceNote_SurvivesTheAdjusters()
        {
            ReviewInput input = Input();
            input.Cpu.Price = 0f;
            var pending = new PendingReview { cpuSaveId = 7, companyKey = "player", releasedOn = "1995-03-01", publishOn = "1995-03-15" };
            AdjusterSet set = Set(Step("a", d => d.Notes.Add("press.note.knownFlaw")));
            ReviewRecord record = ReviewComposer.Compose(pending, "Intelligent", input, Day, PressContentFixture.Outlets(), PressContentFixture.Config(), new QuietRandom(), set);

            Assert.Equal(new List<string> { ReviewComposer.NoPriceNoteKey, "press.note.knownFlaw" }, record.notes);
        }
    }
}
