using System;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The part of a draft of a review that the correction of this feature touches: <c>ReviewDraft</c> of <c>press-reviews</c> in the game, a fake in a test. The press
    /// holds the total correction to +-20 points and drops a note whose key it does not know.
    /// </summary>
    public interface IFlawReviewDraft
    {
        /// <summary>The correction of the total of the review, points (the adjusters add to it).</summary>
        float OverallAdjustment { get; set; }

        /// <summary>Adds a note (a key of the string tables, <c>press.note.…</c>) to the card of the review.</summary>
        void AddNote(string noteKey);
    }

    /// <summary>
    /// The correction of the reviews of the press for a CPU with a known flaw (FR-18, design.md formula 9, Component 16). Pure: the game side
    /// (<c>FlawPressAdjuster</c>, registered with <c>PressReviewsApi.RegisterAdjuster</c>) finds the record of the CPU and hands over the draft.
    /// <list type="bullet">
    /// <item>A flaw that is known and not closed lowers the total by <c>press.overallAdjustment[severity]</c> (-8 / -15 / -15 in the shipped content) and adds the note
    /// "The press knows about a flaw" (FR-18 item 2).</item>
    /// <item>A <see cref="FlawState.Hidden"/> flaw (the registry never hands one out) and a closed one (<see cref="FlawState.Resolved"/>, <see cref="FlawState.Expired"/>,
    /// <see cref="FlawState.Closed"/>) change nothing (FR-18 item 3).</item>
    /// <item>A flaw of an AI company that only the company knows (<see cref="FlawState.InternalKnown"/> or a quiet revision or patch that never was public) changes nothing
    /// either: the press cannot write about it, and a lower mark would be a rumour (FR-11 item 5).</item>
    /// <item>The feature never writes an article: <c>press-reviews</c> does that on <c>OnFlawStateChanged</c> (FR-18 item 4).</item>
    /// </list>
    /// </summary>
    public static class FlawPressRules
    {
        /// <summary>The id of the adjuster (<c>IReviewAdjuster.Id</c>) - the id of the feature.</summary>
        public const string AdjusterId = "design-flaw-recalls";

        /// <summary>The note on the card of the review (it is in the string tables of <c>press-reviews</c>).</summary>
        public const string NoteKey = "press.note.knownFlaw";

        /// <summary>The flaw is known, not closed, and the press can know about it.</summary>
        public static bool Applies(FlawRecordView view)
        {
            if (view == null || view.State == FlawState.Hidden || FlawStateMachine.IsFinal(view.State)) return false;
            if (CompanyKey.IsPlayer(view.CompanyKey)) return true;

            // A rival: only what has been public (or is public now).
            return view.PublicSinceDay > 0;
        }

        /// <summary>The correction for a severity, points (negative): <c>press.overallAdjustment[severity]</c>; 0 for a broken table.</summary>
        public static float Adjustment(FlawSeverity severity, FlawBalance balance)
        {
            if (balance == null || balance.Press == null) return 0f;
            return FlawMath.At(balance.Press.OverallAdjustment, FlawMath.SeverityIndex(severity), 0);
        }

        /// <summary>
        /// Corrects the draft for the flaw of the CPU. <paramref name="view"/> is the record that covers the CPU (<c>FlawRegistry.FindByCpu</c>, <c>null</c> for none).
        /// Returns <c>true</c> when the draft was changed.
        /// </summary>
        public static bool Apply(FlawRecordView view, FlawBalance balance, IFlawReviewDraft draft)
        {
            if (draft == null) throw new ArgumentNullException("draft");
            if (!Applies(view)) return false;

            float adjustment = Adjustment(view.Severity, balance);
            if (adjustment != 0f) draft.OverallAdjustment += adjustment;
            draft.AddNote(NoteKey);
            return true;
        }
    }
}
