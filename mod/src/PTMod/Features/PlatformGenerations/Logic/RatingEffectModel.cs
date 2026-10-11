using System;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>What the rating multiplier of one CPU depends on (FR-5): the bases and the chronology facts of its company and socket, as numbers.</summary>
    public struct RatingInput
    {
        /// <summary><c>IB[company][socket]</c>: the base of the CPU's company on its socket.</summary>
        public double OwnIb;

        /// <summary><c>IB[·][socket]</c>: the base of all companies on the socket.</summary>
        public double SocketIb;

        /// <summary><c>IB_total</c>: the whole installed base.</summary>
        public double TotalIb;

        /// <summary>The socket is a standard package (common to all companies), so the others' base on it counts for <c>sharedWeight</c>.</summary>
        public bool IsStandard;

        /// <summary>The CPU's company is an early adopter of the CPU's generation.</summary>
        public bool IsEarlyAdopter;

        /// <summary>Months since the company became an early adopter.</summary>
        public int MonthsSinceEarly;

        /// <summary>The strength 0..1 of the socket-change penalty of the CPU's socket for the CPU's company (0 = none).</summary>
        public float ChurnSeverity;

        /// <summary>Months since the company's first CPU on the socket.</summary>
        public int MonthsSinceIntro;
    }

    /// <summary>The parts of the rating multiplier and the two results (formulas F2-F6).</summary>
    public struct RatingBreakdown
    {
        /// <summary><c>B_ib</c>: the installed-base bonus.</summary>
        public float Bib;

        /// <summary><c>B_early</c>: the early adopter bonus, faded.</summary>
        public float Bearly;

        /// <summary><c>P_churn</c>: the socket-change penalty, faded (a positive number that is subtracted).</summary>
        public float Pchurn;

        /// <summary><c>m</c> for the consumer segments (HighEnd, MidRange, LowEnd; <c>w_seg = 1</c>).</summary>
        public float ConsumerMult;

        /// <summary><c>m</c> for the Industries segment (<c>w_seg = industriesWeight</c>).</summary>
        public float IndustriesMult;

        /// <summary><c>effShare</c> (F2).</summary>
        public double EffShare;
    }

    /// <summary>The value of the channel <c>market.rating.cpu</c> for one CPU: the formulas F2-F6 of design.md. Pure arithmetic over <see cref="RatingInput"/>.</summary>
    public static class RatingEffectModel
    {
        /// <summary>
        /// F2: <c>effShare = (own + sharedWeight·(socket − own)·[standard]) / total</c>; 0 when the total is 0. The result is kept in [0, 1]
        /// (a socket base below the own base, which the bookkeeping should not give, counts as equal to it).
        /// </summary>
        public static double EffShare(double ownIb, double socketIb, double totalIb, bool isStandard, PlatformBalance b)
        {
            if (!(totalIb > 0)) return 0;

            PlatformBalance.RatingBalance r = (b ?? PlatformBalance.Defaults()).Rating;
            double own = ownIb > 0 ? ownIb : 0;
            double others = socketIb > own ? socketIb - own : 0;
            double share = (own + (isStandard ? r.SharedWeight * others : 0)) / totalIb;
            if (share < 0) return 0;
            return share > 1 ? 1 : share;
        }

        /// <summary>F2-F6 for one CPU. All members of <paramref name="b"/> come from <c>balance.json</c>; <paramref name="b"/> null = the defaults.</summary>
        public static RatingBreakdown Compute(RatingInput i, PlatformBalance b)
        {
            PlatformBalance.RatingBalance r = (b ?? PlatformBalance.Defaults()).Rating;

            double effShare = EffShare(i.OwnIb, i.SocketIb, i.TotalIb, i.IsStandard, b);

            // F3
            double bib = r.IbMaxBonus * Math.Min(1.0, effShare / r.IbSaturationShare);

            // F4
            double bearly = 0;
            if (i.IsEarlyAdopter)
            {
                bearly = r.EarlyBonus * Fade(i.MonthsSinceEarly, r.EarlyDurationMonths);
            }

            // F5
            double pchurn = 0;
            if (i.ChurnSeverity > 0)
            {
                double severity = Math.Min(1.0, i.ChurnSeverity);
                pchurn = r.ChurnPenalty * severity * Fade(i.MonthsSinceIntro, r.ChurnDurationMonths);
            }

            double delta = bib + bearly - pchurn;
            return new RatingBreakdown
            {
                Bib = (float)bib,
                Bearly = (float)bearly,
                Pchurn = (float)pchurn,
                ConsumerMult = Clamp(1.0 + delta, r),
                IndustriesMult = Clamp(1.0 + r.IndustriesWeight * delta, r),
                EffShare = effShare,
            };
        }

        /// <summary><c>max(0, 1 − months / duration)</c>; a negative number of months counts as 0, a duration of 0 or less as "gone at once".</summary>
        private static double Fade(int months, int durationMonths)
        {
            if (durationMonths <= 0) return 0;
            int m = Math.Max(0, months);
            double left = 1.0 - (double)m / durationMonths;
            return left > 0 ? left : 0;
        }

        /// <summary>F6: the guard range of the feature (<c>minMult</c>..<c>maxMult</c>).</summary>
        private static float Clamp(double value, PlatformBalance.RatingBalance r)
        {
            if (double.IsNaN(value)) return 1f;
            if (value < r.MinMult) return r.MinMult;
            if (value > r.MaxMult) return r.MaxMult;
            return (float)value;
        }
    }
}
