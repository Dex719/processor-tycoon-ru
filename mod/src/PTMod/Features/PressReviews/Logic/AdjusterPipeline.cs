using System;
using System.Collections.Generic;

namespace PTMod.Features.PressReviews
{
    /// <summary>One adjuster of another feature as the pipeline calls it: its id and what to run (a closure over the CPU being reviewed).</summary>
    public sealed class AdjusterStep
    {
        public string Id = string.Empty;

        /// <summary>Changes the draft it is given (a copy: whatever it does to the copy is checked before it counts).</summary>
        public Action<ReviewDraft> Run;
    }

    /// <summary>What the composer needs to run the adjusters of other features on the draft of a review.</summary>
    public sealed class AdjusterSet
    {
        /// <summary>The adjusters in the order they were registered; null or empty for none.</summary>
        public IReadOnlyList<AdjusterStep> Steps;

        /// <summary>Is the key of a note in the string tables? A note the tables lack would read "[key]" on the card and is dropped. Null: every note is accepted.</summary>
        public Func<string, bool> IsNoteKnown;

        /// <summary>Told when an adjuster threw (its id and the exception); its correction is skipped. Null: the failure is only counted.</summary>
        public Action<string, Exception> OnError;
    }

    /// <summary>What <see cref="AdjusterPipeline.Apply"/> did.</summary>
    public sealed class AdjusterReport
    {
        /// <summary>How many adjusters ran without an exception.</summary>
        public int Applied;

        /// <summary>How many threw; their corrections were skipped.</summary>
        public int Failed;

        /// <summary>The ids of the adjusters that threw.</summary>
        public readonly List<string> FailedIds = new List<string>();

        /// <summary>How many corrections the limits cut (an axis or the total asked for more than the limit allows).</summary>
        public int Clipped;

        /// <summary>The draft is different after the adjusters (an axis, the total or a note changed).</summary>
        public bool Changed;
    }

    /// <summary>
    /// The adjusters of other features on the draft of a review (FR-11 item 3, 5, design.md Component 11): each adjuster gets a copy of the draft as the
    /// previous ones left it and may move the four axes, the total and add notes. What comes back is checked before it counts:
    /// <list type="bullet">
    /// <item>an axis moves by at most <see cref="AdjusterLimits.Axis"/> points per adjuster and by at most that much in all from its computed value,
    /// and stays within 0…100; the correction of the total (<see cref="ReviewDraft.OverallAdjustment"/>) is held to ±<see cref="AdjusterLimits.Overall"/>
    /// the same way; a number that is not usable (NaN, infinity) counts as no change;</item>
    /// <item>a note is kept when it is a non-empty key of the tables (the caller says which), at most <see cref="MaxNoteLength"/> characters, not there
    /// yet, and there are fewer than <see cref="MaxNotes"/> already; an adjuster cannot take a note away;</item>
    /// <item>an adjuster that throws changes nothing (its copy is dropped), the error goes to <see cref="AdjusterSet.OnError"/> and the next adjuster runs.</item>
    /// </list>
    /// When an axis moved, the weighted total and its cap by the weakest axis are worked out again. Pure: the game never comes in.
    /// </summary>
    public static class AdjusterPipeline
    {
        /// <summary>The most notes a review may carry.</summary>
        public const int MaxNotes = 6;

        /// <summary>The longest key of a note.</summary>
        public const int MaxNoteLength = 80;

        /// <summary>
        /// Runs the adjusters of <paramref name="set"/> on <paramref name="draft"/> and leaves the checked result in it. <paramref name="weights"/> and
        /// <paramref name="cap"/> are those of the segment of the review (they bring <see cref="ReviewDraft.BaseScore"/> and
        /// <see cref="ReviewDraft.CappedScore"/> up to date; null weights leave both as they were).
        /// </summary>
        public static AdjusterReport Apply(ReviewDraft draft, AdjusterSet set, AdjusterLimits limits, float[] weights, CapConfig cap)
        {
            if (draft == null) throw new ArgumentNullException("draft");

            var report = new AdjusterReport();
            if (set == null || set.Steps == null || set.Steps.Count == 0) return report;

            AdjusterLimits rule = limits ?? new AdjusterLimits();
            float axisLimit = Limit(rule.Axis);
            float overallLimit = Limit(rule.Overall);

            float[] computed = Axes(draft.AxisScores, null);
            float[] axes = (float[])computed.Clone();
            float overall = Hold(Finite(draft.OverallAdjustment, 0f), overallLimit);
            var notes = draft.Notes == null ? new List<string>() : new List<string>(draft.Notes);
            int noteCount = notes.Count;

            for (int s = 0; s < set.Steps.Count; s++)
            {
                AdjusterStep step = set.Steps[s];
                if (step == null || step.Run == null) continue;

                var trial = new ReviewDraft
                {
                    AxisScores = (float[])axes.Clone(),
                    BaseScore = draft.BaseScore,
                    CappedScore = draft.CappedScore,
                    NoAnalog = draft.NoAnalog,
                    Notes = new List<string>(notes),
                    OverallAdjustment = overall,
                };

                try
                {
                    step.Run(trial);
                }
                catch (Exception ex)
                {
                    report.Failed++;
                    report.FailedIds.Add(step.Id ?? string.Empty);
                    Tell(set, step.Id, ex);
                    continue;
                }

                report.Applied++;
                float[] asked = Axes(trial.AxisScores, axes);
                for (int i = 0; i < ReviewMath.AxisCount; i++)
                {
                    float next = Move(axes[i], asked[i], axisLimit, computed[i], 0f, 100f, ref report.Clipped);
                    axes[i] = next;
                }
                overall = Move(overall, Finite(trial.OverallAdjustment, overall), overallLimit, 0f, -overallLimit, overallLimit, ref report.Clipped);
                MergeNotes(notes, trial.Notes, set.IsNoteKnown);
            }

            Write(draft, axes, overall, notes, weights, cap, report, noteCount);
            return report;
        }

        // ---- internals ----

        /// <summary>
        /// The value after an adjuster asked for <paramref name="asked"/>: the step is cut to ±<paramref name="limit"/>, then the whole distance from
        /// <paramref name="origin"/> to ±<paramref name="limit"/>, then to the scale [<paramref name="low"/>, <paramref name="high"/>].
        /// </summary>
        private static float Move(float current, float asked, float limit, float origin, float low, float high, ref int clipped)
        {
            double step = asked - (double)current;
            double allowed = step;
            if (allowed > limit) allowed = limit;
            if (allowed < -limit) allowed = -limit;
            if (allowed != step) clipped++;

            double value = current + allowed;
            double nearest = Math.Max(origin - limit, Math.Min(origin + limit, value));
            if (nearest != value) clipped++;

            value = Math.Max(low, Math.Min(high, nearest));
            return (float)value;
        }

        private static void MergeNotes(List<string> notes, List<string> offered, Func<string, bool> isKnown)
        {
            if (offered == null) return;

            for (int i = 0; i < offered.Count && notes.Count < MaxNotes; i++)
            {
                string note = offered[i];
                if (string.IsNullOrWhiteSpace(note) || note.Length > MaxNoteLength || notes.Contains(note)) continue;
                if (isKnown != null && !Known(isKnown, note)) continue;
                notes.Add(note);
            }
        }

        private static bool Known(Func<string, bool> isKnown, string note)
        {
            try
            {
                return isKnown(note);
            }
            catch (Exception)
            {
                return true;   // a check that fails must not cost the review its note
            }
        }

        private static void Write(ReviewDraft draft, float[] axes, float overall, List<string> notes, float[] weights, CapConfig cap, AdjusterReport report, int notesBefore)
        {
            bool axesMoved = false;
            if (draft.AxisScores == null || draft.AxisScores.Length < ReviewMath.AxisCount) draft.AxisScores = new float[ReviewMath.AxisCount];
            for (int i = 0; i < ReviewMath.AxisCount; i++)
            {
                if (draft.AxisScores[i] != axes[i]) axesMoved = true;
                draft.AxisScores[i] = axes[i];
            }

            bool overallMoved = draft.OverallAdjustment != overall;
            draft.OverallAdjustment = overall;

            bool notesAdded = notes.Count != notesBefore;
            draft.Notes = notes;

            if (axesMoved && weights != null)
            {
                draft.BaseScore = ReviewMath.WeightedScore(draft.AxisScores, weights);
                draft.CappedScore = ReviewMath.ApplyCap(draft.BaseScore, draft.AxisScores, weights, cap);
            }
            report.Changed = axesMoved || overallMoved || notesAdded;
        }

        private static float[] Axes(float[] source, float[] fallback)
        {
            var result = new float[ReviewMath.AxisCount];
            for (int i = 0; i < result.Length; i++)
            {
                float own = fallback != null ? fallback[i] : 0f;
                result[i] = source != null && i < source.Length ? Finite(source[i], own) : own;
            }
            return result;
        }

        private static float Finite(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }

        private static float Limit(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? 0f : value;
        }

        private static float Hold(float value, float limit)
        {
            return value > limit ? limit : value < -limit ? -limit : value;
        }

        private static void Tell(AdjusterSet set, string id, Exception ex)
        {
            if (set.OnError == null) return;
            try
            {
                set.OnError(id ?? string.Empty, ex);
            }
            catch (Exception)
            {
                // the report of a failure must not become a second failure
            }
        }
    }
}
