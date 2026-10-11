using System;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The socket-change penalty strength (FR-6 item 2): how bad it is to leave a main socket that the company has supported for too short a
    /// time. Pure arithmetic; the timeline finds the main socket, its age and share and whether the new socket is compatible with it, and asks here.
    /// The penalty itself (<c>P_churn</c>, formula F5) is worked out by <see cref="RatingEffectModel"/> from this severity.
    /// </summary>
    public static class ChurnDetector
    {
        /// <summary>
        /// <c>severity = 1 − age / minSocketLifeMonths</c> when the company has a main socket whose share of the whole base is at least
        /// <c>churnMinBaseShare</c>, whose age is below <c>minSocketLifeMonths</c> and which the new socket is not compatible with
        /// (<see cref="SocketIdentity.Compatible"/>); otherwise 0. The result is in [0, 1]: a main socket of age 0 gives 1. A negative age counts as 0,
        /// a share that is NaN counts as too small. <paramref name="b"/> null = the defaults.
        /// </summary>
        public static float Severity(bool hasMain, int mainAgeMonths, double mainShare, bool compatible, PlatformBalance b)
        {
            if (!hasMain || compatible) return 0f;

            PlatformBalance.RatingBalance r = (b ?? PlatformBalance.Defaults()).Rating;
            if (!(mainShare >= r.ChurnMinBaseShare)) return 0f;   // also false for NaN
            if (r.MinSocketLifeMonths <= 0 || mainAgeMonths >= r.MinSocketLifeMonths) return 0f;

            int age = Math.Max(0, mainAgeMonths);
            double severity = 1.0 - (double)age / r.MinSocketLifeMonths;
            if (severity < 0) return 0f;
            if (severity > 1) return 1f;
            return (float)severity;
        }
    }
}
