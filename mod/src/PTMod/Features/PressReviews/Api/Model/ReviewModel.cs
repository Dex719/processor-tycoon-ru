using System;
using System.Collections.Generic;

namespace PTMod.Features.PressReviews
{
    // Pure public model of press-reviews (no Unity, no game types): the types the public API hands out or takes in. Everything in Api/Model/ must stay
    // free of UnityEngine and Assembly-CSharp. The features that use the API are built with the folder Api alone, so these types live here and not in
    // Logic/; the unit-test build compiles this folder together with Logic/.

    /// <summary>The four axes of a review, in the order of every array of the feature (scores, weights, weight shifts).</summary>
    public enum Axis
    {
        Performance = 0,
        Value = 1,
        Efficiency = 2,
        Thermal = 3,
    }

    /// <summary>
    /// The result of the review of one CPU before the outlets mark it: the four axes, the weighted total and the total capped by the weakest axis. An
    /// <see cref="IReviewAdjuster"/> of another feature may move the axes (by up to 30 points each), the total (by up to 20 points) and add notes; the
    /// formulas leave <see cref="Notes"/> and <see cref="OverallAdjustment"/> to the adjusters.
    /// </summary>
    public sealed class ReviewDraft
    {
        /// <summary>The scores 0…100 in the order of <see cref="Axis"/>.</summary>
        public float[] AxisScores = new float[4];

        /// <summary>The weighted total with the weights of the segment.</summary>
        public float BaseScore;

        /// <summary>The total after the cap by the weakest axis.</summary>
        public float CappedScore;

        /// <summary>No reference CPU was found: the performance axis is the fixed score and the other two ratio axes are at parity.</summary>
        public bool NoAnalog;

        /// <summary>Notes of the adjusters (string table keys of the feature <c>press-reviews</c>, <c>press.note.…</c>); the card of the review shows them.</summary>
        public List<string> Notes = new List<string>();

        /// <summary>The adjusters' correction of the total, in points; the feature holds the sum to ±20.</summary>
        public float OverallAdjustment;
    }

    /// <summary>The mark of one outlet in a review.</summary>
    [Serializable]
    public sealed class OutletLine
    {
        public string outletId;
        public int score;
        public string quoteKey;
    }

    /// <summary>
    /// A published review, as the archive keeps it and as <c>PressReviewsApi.ReviewPublished</c> and <c>GetReview</c> hand it out (a copy: changing it
    /// changes nothing in the archive). Plain data with public lower-case fields, bound to JSON by <c>ModJson</c>.
    /// </summary>
    [Serializable]
    public sealed class ReviewRecord
    {
        public int cpuSaveId;
        public string cpuName;

        /// <summary><c>player</c> or <c>ai:&lt;UniqueID&gt;</c> (<c>ModCompanies.KeyOf</c>).</summary>
        public string companyKey;

        public string companyName;
        public string releasedOn;
        public string publishedOn;

        /// <summary><c>(int)Segment</c>: 0 High End, 1 Mid Range, 2 Low End, 3 Industries.</summary>
        public int segment;

        /// <summary>The four axes; empty when the record is compact.</summary>
        public float[] axes = new float[4];

        /// <summary>The mean of the marks of the outlets, 0…100.</summary>
        public int metascore;

        public string verdictKey;
        public bool noAnalog;

        /// <summary>The marks of the outlets; null when the record is compact.</summary>
        public List<OutletLine> outlets;

        /// <summary>The save ids of the reference CPUs; null when the record is compact.</summary>
        public List<int> peerSaveIds;

        /// <summary>The notes of the adjusters (string table keys).</summary>
        public List<string> notes;

        public int reputationDelta;
        public float hypeAtPublish;

        /// <summary>The id of the mail with the review (0 for the AI's reviews).</summary>
        public int mailId;

        public bool compact;
    }
}
