using PTMod.Core;
using PTMod.Features.PressReviews;
using GameCpu = ProcessorTycoon.Hardware.Cpu;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The correction of the reviews of <c>press-reviews</c> for a CPU with a known flaw (FR-18, design.md Component 16): what <c>IReviewAdjuster.Adjust(cpu, draft)</c> does.
    /// The press asks it before it works out the marks of the outlets of a review. The rules are the pure <see cref="FlawPressRules"/>: a flaw that is known and not closed lowers
    /// the total by <c>press.overallAdjustment[severity]</c> (-8 / -15 / -15) and adds the note "The press knows about a flaw"; a hidden or closed flaw changes nothing. The
    /// feature never writes an article: <c>press-reviews</c> does that on <c>OnFlawStateChanged</c>.
    /// <para>
    /// It has no side effects and it is cheap (a lookup in the registry and one snapshot), as the contract of the interface asks. <see cref="FlawPressBridge"/> registers the
    /// object with <c>PressReviewsApi.RegisterAdjuster</c> while both features are on. The class lives in the namespace of this feature, so a failure of <see cref="Adjust"/> is
    /// counted against <c>design-flaw-recalls</c> (the press reports it to the feature that owns the class of the adjuster).
    /// </para>
    /// </summary>
    internal sealed class FlawPressAdjuster : IReviewAdjuster
    {
        private readonly DesignFlawRecallsFeature _feature;

        public FlawPressAdjuster(DesignFlawRecallsFeature feature)
        {
            _feature = feature;
        }

        /// <summary>The id the press knows the adjuster by: the id of the feature (AC-18.1).</summary>
        public string Id
        {
            get { return FlawPressRules.AdjusterId; }
        }

        /// <summary>Corrects the draft of the review of <paramref name="cpu"/> in place. An exception is the press's to catch and count against the feature.</summary>
        public void Adjust(GameCpu cpu, ReviewDraft draft)
        {
            if (cpu == null || draft == null || !ModFeatures.IsActive(DesignFlawRecallsFeature.FeatureId)) return;

            FlawRecordView view = DesignFlawRecallsApi.Registry.FindByCpu(cpu.SaveID);
            if (view == null) return;   // no flaw, a hidden one (the registry never shows it) or the registry is not there

            FlawPressRules.Apply(view, _feature.Balance, new DraftOfThePress(draft));
        }

        /// <summary>The draft of the press seen through <see cref="IFlawReviewDraft"/>: its <c>OverallAdjustment</c> and its list of notes.</summary>
        private sealed class DraftOfThePress : IFlawReviewDraft
        {
            private readonly ReviewDraft _draft;

            public DraftOfThePress(ReviewDraft draft)
            {
                _draft = draft;
            }

            public float OverallAdjustment
            {
                get { return _draft.OverallAdjustment; }
                set { _draft.OverallAdjustment = value; }
            }

            public void AddNote(string noteKey)
            {
                if (_draft.Notes == null) _draft.Notes = new System.Collections.Generic.List<string>();
                if (!_draft.Notes.Contains(noteKey)) _draft.Notes.Add(noteKey);
            }
        }
    }
}
