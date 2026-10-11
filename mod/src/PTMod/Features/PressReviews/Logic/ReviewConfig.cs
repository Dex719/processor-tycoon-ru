using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.PressReviews
{
    // The numbers of the review formulas (design.md "Контент: review-config.json"). The built-in file Content/press-reviews/review-config.json
    // holds the same values as ReviewConfig.CreateDefault(); the file is what the player can override, the defaults are what the code falls back
    // to when the file is broken. The segment rule (segments.priceBand) belongs to the core and is not here, neither is the reputation drift.

    /// <summary>How many reference CPUs a review compares with and where it looks for them (FR-2 item 2).</summary>
    public sealed class PeerConfig
    {
        public int Count = 5;
        public int PeriodMonths = 3;
        public int Candidates = 15;
    }

    /// <summary>The growth rate of the market from <see cref="From"/> on: reference CPUs grow by <see cref="G"/> per year (scaled by tau).</summary>
    public sealed class EraRule
    {
        public int From;
        public float G;
    }

    /// <summary>The rising bar (FR-2 item 3): tau and the eras. The eras are sorted by <see cref="EraRule.From"/>.</summary>
    public sealed class TrendConfig
    {
        public float Tau = 0.6f;

        public List<EraRule> Eras = new List<EraRule>
        {
            new EraRule { From = 1971, G = 1.35f },
            new EraRule { From = 1985, G = 1.45f },
            new EraRule { From = 2000, G = 1.30f },
            new EraRule { From = 2010, G = 1.15f },
        };
    }

    /// <summary>The curve of a ratio: <c>clamp(Parity + Span × ln(r / strictness) / ln(RatioAtTop), 0, 100)</c>.</summary>
    public sealed class CurveConfig
    {
        public float Parity = 65f;
        public float Span = 35f;
        public float RatioAtTop = 1.5f;
    }

    /// <summary>The thermal axis (FR-2 item 4): the safe temperature is capped, full marks that many degrees below it, zero that many above.</summary>
    public sealed class ThermalConfig
    {
        public float SafeCap = 95f;
        public float FullMarksBelowSafe = 35f;
        public float ZeroAboveSafe = 5f;
    }

    /// <summary>The cap of the total by the weakest axis (FR-2 item 5): total ≤ weakest axis (with weight ≥ MinWeight) + Gap.</summary>
    public sealed class CapConfig
    {
        public float Gap = 30f;
        public float MinWeight = 0.15f;
    }

    /// <summary>A verdict: the key of its name from the metascore <see cref="Min"/> up.</summary>
    public sealed class VerdictRule
    {
        public int Min;
        public string Key = string.Empty;
    }

    /// <summary>The hype of a reviewed CPU (FR-5): <c>clamp(1 + Strength × (M − Parity)/35 × decay, Min, Max)</c>, for <see cref="DurationDays"/> days.</summary>
    public sealed class HypeConfig
    {
        public float Parity = 65f;
        public float Strength = 0.5f;
        public float Min = 0.6f;
        public float Max = 1.5f;
        public int DurationDays = 365;
    }

    /// <summary>The reputation change of a review (FR-6): <c>clamp(round((M − Parity)/Divisor), Min, Max)</c>.</summary>
    public sealed class RepDeltaConfig
    {
        public float Parity = 65f;
        public float Divisor = 5f;
        public int Min = -6;
        public int Max = 6;
    }

    /// <summary>The effect of the reputation on popularity (FR-7): <c>1 + Strength × (rep − Neutral)/50</c>; <see cref="AffectsAI"/> is the default of the parameter.</summary>
    public sealed class ReputationEffectConfig
    {
        public float Strength = 0.3f;
        public float Neutral = 50f;
        public bool AffectsAI = true;
    }

    /// <summary>The yearly awards (FR-8).</summary>
    public sealed class AwardsConfig
    {
        public int CpuOfYearMin = 75;
        public int CpuOfYearRep = 4;
        public int CompanyOfYearMinReviews = 2;
        public int CompanyOfYearRep = 3;
        public int DisappointmentMax = 45;
        public int DisappointmentRep = -3;
    }

    /// <summary>A level of the reputation: the key of its name from the reputation <see cref="Min"/> up.</summary>
    public sealed class ReputationLevel
    {
        public int Min;
        public string Key = string.Empty;
    }

    /// <summary>The digest of the rivals' reviews (FR-4).</summary>
    public sealed class DigestConfig
    {
        public int MaxLines = 10;
        public int NotableHigh = 85;
        public int NotableLow = 40;
        public int Parity = 65;
    }

    /// <summary>The size of the archive of reviews: older than <see cref="MaxFull"/> are stored compact, at most <see cref="MaxTotal"/> are kept.</summary>
    public sealed class ArchiveConfig
    {
        public int MaxFull = 2000;
        public int MaxTotal = 5000;
    }

    /// <summary>How far an adjuster of another feature may move an axis (±Axis) and the total (±Overall).</summary>
    public sealed class AdjusterLimits
    {
        public float Axis = 30f;
        public float Overall = 20f;
    }

    /// <summary>The chance that a published review tries to reveal a hidden flaw (FR-11 item 6).</summary>
    public sealed class FlawRevealConfig
    {
        public float Chance = 0.10f;
    }

    /// <summary>
    /// All the numbers of the review formulas and the text keys of the verdicts, reputation levels and quotes. Built by
    /// <see cref="PressContentValidator.ParseConfig"/> from <c>review-config.json</c>; a value that is missing or unusable keeps the default
    /// of <see cref="CreateDefault"/>. Plain data, no behavior beyond lookups.
    /// </summary>
    public sealed class ReviewConfig
    {
        /// <summary>The number of quote pools: four axes, each with a "high" and a "low" list.</summary>
        public const int QuotePoolCount = 8;

        public int DelayMinDays = 7;
        public int DelayMaxDays = 21;
        public PeerConfig Peers = new PeerConfig();
        public TrendConfig Trend = new TrendConfig();
        public CurveConfig Curve = new CurveConfig();

        /// <summary>The strictness by <c>Player.DifficultyLevel</c> 0…5.</summary>
        public float[] Strictness = { 0.85f, 0.92f, 1.00f, 1.08f, 1.16f, 1.25f };

        public ThermalConfig Thermal = new ThermalConfig();

        /// <summary>The weights of the axes per segment: <c>Weights[(int)segment][(int)axis]</c>, normalized to a sum of 1.</summary>
        public float[][] Weights = DefaultWeights();

        public CapConfig Cap = new CapConfig();

        /// <summary>The performance axis of a CPU nothing can be compared with.</summary>
        public float NoAnalogPerformance = 70f;

        /// <summary>The verdicts, the highest <see cref="VerdictRule.Min"/> first.</summary>
        public List<VerdictRule> Verdicts = new List<VerdictRule>
        {
            new VerdictRule { Min = 90, Key = "press.verdict.masterpiece" },
            new VerdictRule { Min = 80, Key = "press.verdict.excellent" },
            new VerdictRule { Min = 70, Key = "press.verdict.good" },
            new VerdictRule { Min = 55, Key = "press.verdict.average" },
            new VerdictRule { Min = 40, Key = "press.verdict.weak" },
            new VerdictRule { Min = 0, Key = "press.verdict.flop" },
        };

        public HypeConfig Hype = new HypeConfig();
        public RepDeltaConfig ReputationPerReview = new RepDeltaConfig();
        public ReputationEffectConfig ReputationEffect = new ReputationEffectConfig();
        public AwardsConfig Awards = new AwardsConfig();

        /// <summary>The levels of the reputation, the highest <see cref="ReputationLevel.Min"/> first.</summary>
        public List<ReputationLevel> ReputationLevels = new List<ReputationLevel>
        {
            new ReputationLevel { Min = 80, Key = "press.rep.cult" },
            new ReputationLevel { Min = 65, Key = "press.rep.respected" },
            new ReputationLevel { Min = 36, Key = "press.rep.ordinary" },
            new ReputationLevel { Min = 21, Key = "press.rep.dubious" },
            new ReputationLevel { Min = 0, Key = "press.rep.toxic" },
        };

        public DigestConfig Digest = new DigestConfig();
        public ArchiveConfig Archive = new ArchiveConfig();
        public AdjusterLimits AdjusterLimits = new AdjusterLimits();
        public FlawRevealConfig FlawReveal = new FlawRevealConfig();

        /// <summary>The quote pools, index <c>axis × 2</c> for "high" and <c>axis × 2 + 1</c> for "low". Empty by default: the quotes are content.</summary>
        public List<string>[] QuotePools = NewQuotePools();

        /// <summary>The configuration with the values of the design (the same numbers as the built-in <c>review-config.json</c>, without the quotes).</summary>
        public static ReviewConfig CreateDefault()
        {
            return new ReviewConfig();
        }

        internal static float[][] DefaultWeights()
        {
            return new[]
            {
                new[] { 0.50f, 0.20f, 0.15f, 0.15f },   // HighEnd
                new[] { 0.35f, 0.35f, 0.15f, 0.15f },   // MidRange
                new[] { 0.25f, 0.45f, 0.15f, 0.15f },   // LowEnd
                new[] { 0.20f, 0.30f, 0.40f, 0.10f },   // Industries
            };
        }

        internal static List<string>[] NewQuotePools()
        {
            var pools = new List<string>[QuotePoolCount];
            for (int i = 0; i < pools.Length; i++) pools[i] = new List<string>();
            return pools;
        }

        /// <summary>The weights of the four axes for a segment (the same array each time, do not change it); <see cref="Segment.Any"/> uses Mid Range.</summary>
        public float[] WeightsFor(Segment segment)
        {
            int index = (int)segment;
            if (index < 0 || index >= Weights.Length) index = (int)Segment.MidRange;
            return Weights[index];
        }

        /// <summary>The strictness for a difficulty level; a level outside the table uses the nearest end of it.</summary>
        public float StrictnessFor(int difficulty)
        {
            if (Strictness == null || Strictness.Length == 0) return 1f;
            int index = difficulty < 0 ? 0 : difficulty >= Strictness.Length ? Strictness.Length - 1 : difficulty;
            return Strictness[index];
        }

        /// <summary>The quote keys for an axis and a side ("high" = the axis is strong, "low" = weak); never null.</summary>
        public IList<string> QuotesFor(Axis axis, bool high)
        {
            int index = (int)axis * 2 + (high ? 0 : 1);
            if (QuotePools == null || index < 0 || index >= QuotePools.Length || QuotePools[index] == null) return new string[0];
            return QuotePools[index];
        }
    }
}
