using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Content;

namespace PTMod.Features.PressReviews
{
    // The records of the content files of press-reviews (FR-3, FR-10): outlets.json and notes.json are record files
    // ({ "schema": 1, "records": [ ... ] }, ModContent), review-config.json is a config object (ReviewConfig.cs). The classes are plain data:
    // the parsers and validators are in PressContentValidator, the game-side loader is Content/PressContent.cs. The files are in the Logic
    // folder because the test build compiles only that folder, and the content validation has to be tested (TSK-002).

    /// <summary>
    /// A press outlet (FR-3): which years it works, which segments it covers, how its taste shifts the weights of the four axes, its bias
    /// and the noise of its marks. <see cref="WeightShift"/> is in the order of <see cref="Axis"/>. The outlets are fictional (owner decision).
    /// </summary>
    public sealed class Outlet : IContentRecord
    {
        private string id = string.Empty;

        /// <summary>The technical id (<c>kilobyte</c>); unique, never shown to the player.</summary>
        public string Id
        {
            get { return id; }
            set { id = value ?? string.Empty; }
        }

        /// <summary>The name as the player reads it (inline ru/en, optionally with a table key).</summary>
        public LocText Name;

        /// <summary>The first and the last year the outlet works (both included).</summary>
        public int YearFrom;
        public int YearTo;

        /// <summary>The segments the outlet reviews.</summary>
        public List<Segment> Segments = new List<Segment>();

        /// <summary>The shift of the segment weights per axis (performance, value, efficiency, thermal); the shifted weights are normalized.</summary>
        public float[] WeightShift = new float[4];

        /// <summary>Added to the outlet's mark.</summary>
        public float Bias;

        /// <summary>The standard deviation of the noise added to the mark.</summary>
        public float NoiseSigma = 3f;

        /// <summary>The outlet stands in for a segment nobody covers in the years <see cref="FallbackFrom"/>…<see cref="FallbackTo"/>.</summary>
        public bool HasFallback;
        public int FallbackFrom;
        public int FallbackTo;

        public bool WorksIn(int year)
        {
            return year >= YearFrom && year <= YearTo;
        }

        public bool Reviews(Segment segment)
        {
            return Segments.Contains(segment);
        }

        /// <summary>The outlet works in the year and reviews the segment.</summary>
        public bool IsActiveFor(int year, Segment segment)
        {
            return WorksIn(year) && Reviews(segment);
        }

        /// <summary>The year is inside the interval in which the outlet stands in for a segment nobody covers.</summary>
        public bool IsFallbackIn(int year)
        {
            return HasFallback && year >= FallbackFrom && year <= FallbackTo;
        }
    }

    /// <summary>The kinds of conditions a note of <c>notes.json</c> may have (FR-10). Anything else is dropped when the file is read.</summary>
    public enum NoteConditionType
    {
        /// <summary>The year-on-year growth of the segment leader's <c>Ips</c> (<c>min</c> / <c>max</c> as a fraction: 0.25 is +25 %).</summary>
        LeaderGrowth = 0,

        /// <summary>The year-on-year change of the median price of the segment (a fraction).</summary>
        MedianPriceChange = 1,

        /// <summary>The market has a PGA CPU for the first time this year.</summary>
        PgaShift = 2,

        /// <summary>A static note about a segment.</summary>
        SegmentFocus = 3,

        /// <summary>The change of the player's reputation over 12 months (points).</summary>
        ReputationTrend = 4,

        /// <summary>A company published its first review this year.</summary>
        NewEntrant = 5,
    }

    /// <summary>The condition of a note: a type with the parameters that type reads.</summary>
    public sealed class NoteCondition
    {
        public NoteConditionType Type;

        /// <summary>The segment the condition is about; <see cref="Segment.Any"/> for the types that have none.</summary>
        public Segment Segment = Segment.Any;

        /// <summary>The lower bound (inclusive), when the file has one.</summary>
        public float? Min;

        /// <summary>The upper bound (inclusive), when the file has one.</summary>
        public float? Max;

        private static readonly string[] Names =
        {
            "leaderGrowth", "medianPriceChange", "pgaShift", "segmentFocus", "reputationTrend", "newEntrant",
        };

        /// <summary>The type from its name in the file ("leaderGrowth"); false for a name the mod does not know.</summary>
        public static bool TryParseType(string name, out NoteConditionType type)
        {
            type = NoteConditionType.LeaderGrowth;
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < Names.Length; i++)
            {
                if (string.Equals(Names[i], name, StringComparison.Ordinal))
                {
                    type = (NoteConditionType)i;
                    return true;
                }
            }
            return false;
        }

        public static string NameOf(NoteConditionType type)
        {
            int index = (int)type;
            return index >= 0 && index < Names.Length ? Names[index] : "unknown";
        }

        /// <summary>The types that need a segment.</summary>
        public static bool NeedsSegment(NoteConditionType type)
        {
            return type == NoteConditionType.LeaderGrowth || type == NoteConditionType.MedianPriceChange || type == NoteConditionType.SegmentFocus;
        }

        /// <summary>The types that are meaningless without at least one of <c>min</c> and <c>max</c>.</summary>
        public static bool NeedsBound(NoteConditionType type)
        {
            return type == NoteConditionType.LeaderGrowth || type == NoteConditionType.MedianPriceChange || type == NoteConditionType.ReputationTrend;
        }
    }

    /// <summary>
    /// A quarterly note of the press (FR-10): a condition, the table key of the text and the names of the values that fill its
    /// <c>{0}</c>, <c>{1}</c>… (only the names of <see cref="KnownArgs"/>; the numbers are given in words, the coefficients are never shown).
    /// </summary>
    public sealed class NoteTemplate : IContentRecord
    {
        /// <summary>The names an <see cref="Args"/> entry may have.</summary>
        public static readonly string[] KnownArgs = { "segment", "qualitativeGrowth", "qualitativeChange", "companyName" };

        private string id = string.Empty;

        public string Id
        {
            get { return id; }
            set { id = value ?? string.Empty; }
        }

        public NoteCondition Condition = new NoteCondition();

        /// <summary>The key of the text in the string tables (<c>press.note.perfBar</c>).</summary>
        public string TextKey = string.Empty;

        public List<string> Args = new List<string>();

        /// <summary>How many quarters the note is not chosen again.</summary>
        public int CooldownQuarters = 4;

        /// <summary>The weight of the note when up to two are chosen among those whose condition holds.</summary>
        public float Weight = 1f;
    }

    /// <summary>
    /// What the validation of the content found: <see cref="Errors"/> (a piece of the content is unusable and was replaced or dropped),
    /// <see cref="Warnings"/> (a value was clipped or ignored) and <see cref="CoverageHoles"/> (a year and a segment that no outlet reviews, "1995 LowEnd").
    /// </summary>
    public sealed class ValidationReport
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> CoverageHoles = new List<string>();

        /// <summary>No errors and no holes (warnings do not count).</summary>
        public bool IsClean
        {
            get { return Errors.Count == 0 && CoverageHoles.Count == 0; }
        }
    }
}
