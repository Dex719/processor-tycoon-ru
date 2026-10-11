using System;
using System.Collections.Generic;

namespace PTMod.Features.PressReviews
{
    // The state of press-reviews that goes into the section "press-reviews" of the side save file (design.md "Записи обзоров и состояние").
    // The classes are plain data with public lower-case fields, bound to JSON by ModJson. There is no version field anywhere: the core writes
    // and reads the version of the section (_schema). Fields named "segment" hold (int)Segment: 0 HighEnd, 1 MidRange, 2 LowEnd, 3 Industries.
    // They are in the Logic folder (not State/) because the test build compiles only that folder and the save round trip has to be tested.

    /// <summary>The whole section. Lists are never null after <see cref="PressStateCodec.Read"/>.</summary>
    [Serializable]
    public sealed class PressState
    {
        /// <summary>The reviews waiting for their date (CPUs of the player wait 7–21 days, CPUs of the AI too).</summary>
        public List<PendingReview> pending = new List<PendingReview>();

        /// <summary>The archive of the published reviews, newest last; the old ones are stored compact.</summary>
        public List<ReviewRecord> reviews = new List<ReviewRecord>();

        public List<YearAwards> awards = new List<YearAwards>();

        /// <summary>The articles other features published (the last 200).</summary>
        public List<ArticleRecord> articles = new List<ArticleRecord>();

        public List<NoteCooldown> noteCooldowns = new List<NoteCooldown>();

        /// <summary>The yearly snapshots of the market for the notes (the last 3 years).</summary>
        public List<SegmentYearStat> yearStats = new List<SegmentYearStat>();

        /// <summary>The month (<c>yyyy-MM</c>) of the last digest of the rivals' reviews; null before the first one.</summary>
        public string lastDigest;
    }

    /// <summary>A review that has been queued and is not published yet.</summary>
    [Serializable]
    public sealed class PendingReview
    {
        public int cpuSaveId;

        /// <summary><c>player</c> or <c>ai:&lt;UniqueID&gt;</c>.</summary>
        public string companyKey;

        /// <summary>The day the CPU was released, <c>yyyy-MM-dd</c>.</summary>
        public string releasedOn;

        /// <summary>The day the review is due, <c>yyyy-MM-dd</c>.</summary>
        public string publishOn;

        /// <summary>What the CPU sold per segment while it waited (the sum of the daily sales), four numbers.</summary>
        public int[] soldBySegment = new int[4];

        /// <summary>How many times the calculation failed (the review is cancelled after 3).</summary>
        public int attempts;
    }

    [Serializable]
    public sealed class YearAwards
    {
        public int year;
        public List<AwardLine> cpuOfYear;
        public string companyOfYearKey;
        public float companyOfYearAvg;
        public int disappointmentCpuSaveId;
        public int disappointmentScore;
    }

    [Serializable]
    public sealed class AwardLine
    {
        public int segment;
        public int cpuSaveId;
        public string cpuName;
        public string companyKey;
        public int score;
    }

    [Serializable]
    public sealed class ArticleRecord
    {
        public string date;
        public string source;
        public string titleKey;
        public string bodyKey;
        public List<string> args;
    }

    /// <summary>A note that was used and is resting until <see cref="until"/> (<c>yyyy-MM-dd</c>).</summary>
    [Serializable]
    public sealed class NoteCooldown
    {
        public string noteId;
        public string until;
    }

    [Serializable]
    public sealed class SegmentYearStat
    {
        public int year;
        public int segment;
        public float leaderIps;
        public float medianPrice;
    }
}
