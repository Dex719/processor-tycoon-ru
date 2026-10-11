using System;
using System.Collections.Generic;
using PTMod.Core;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The yearly awards on the game's side (FR-8, design.md Component 8): on 1 January (<c>TickPhase.LateYearPassed</c>, after the game's own letter
    /// about the year) the press sums up the reviews published in the year that has just ended: "CPU of the Year" per segment, "Company of the Year" and
    /// "Disappointment of the Year" (<see cref="AwardsCalculator"/>). The awards go to the archive (<see cref="PressState.awards"/>), the reputation of the
    /// winners changes (<c>ModReputation.Add</c>, +4 / +3 / −3, the reasons <c>press.award:…</c>), and a letter "Press awards 1995" with the action "Open
    /// the awards" is sent. A year in which nothing was awarded gets no letter and no entry. Each step is in its own guard, so a failure of one does not
    /// take the others with it; a year that is in the archive already is not done again.
    /// </summary>
    public sealed class AwardsService
    {
        private const string Feature = PressReviewsFeature.FeatureId;

        private readonly Func<PressState> stateOf;

        public AwardsService(Func<PressState> stateOf)
        {
            if (stateOf == null) throw new ArgumentNullException("stateOf");
            this.stateOf = stateOf;
        }

        /// <summary>The awards of a year over the given reviews (design.md: the pure part). The effects are not made here.</summary>
        public YearAwards Compute(int year, IReadOnlyList<ReviewRecord> reviewsOfYear, ReviewConfig cfg)
        {
            var list = new List<ReviewRecord>(reviewsOfYear ?? new ReviewRecord[0]);
            return AwardsCalculator.Compute(year, list, (cfg ?? ReviewConfig.CreateDefault()).Awards).Awards;
        }

        /// <summary>
        /// <c>TickPhase.LateYearPassed</c>: the awards of the year before the current one. Does nothing in a game without a date, for a year that has its
        /// awards, and for a year in which nothing was awarded.
        /// </summary>
        public void OnNewYear()
        {
            DateTime today = ModTicks.Today;
            if (today == default(DateTime)) return;

            int year = today.Year - 1;
            PressState state = stateOf();
            if (state.awards == null) state.awards = new List<YearAwards>();
            if (state.awards.Exists(a => a != null && a.year == year))
            {
                ModLog.Debug(Feature, "awards: the awards of " + year + " are made already");
                return;
            }

            AwardsResult result = AwardsCalculator.Compute(year, state.reviews, PressContent.Current.Config.Awards);
            if (result.IsEmpty)
            {
                ModLog.Debug(Feature, "awards: nothing to award for " + year + ", no letter");
                return;
            }

            state.awards.Add(result.Awards);
            if (state.awards.Count > PressStateCodec.MaxAwards) state.awards.RemoveRange(0, state.awards.Count - PressStateCodec.MaxAwards);

            for (int i = 0; i < result.Payouts.Count; i++)
            {
                AwardPayout payout = result.Payouts[i];
                ModHooks.Guard(Feature, "awards/reputation", () => Pay(payout));
            }

            int playerDelta = result.DeltaOf(ModCompanies.PlayerKey);
            ModHooks.Guard(Feature, "awards/mail", () => PressMail.SendAwards(result.Awards, playerDelta));
            ModLog.Info(Feature, "awards: " + year + ": " + result.Awards.cpuOfYear.Count + " CPU(s) of the year, company "
                + (string.IsNullOrEmpty(result.Awards.companyOfYearKey) ? "-" : result.Awards.companyOfYearKey)
                + ", disappointment " + (result.Awards.disappointmentCpuSaveId < 0 ? "-" : result.Awards.disappointmentCpuSaveId.ToString()));
        }

        private static void Pay(AwardPayout payout)
        {
            ICompany company = ModCompanies.Resolve(payout.CompanyKey);
            if (company == null)
            {
                ModLog.Debug(Feature, "awards: the company " + payout.CompanyKey + " is not in the game, its reputation is left alone");
                return;
            }
            ModReputation.Add(company, payout.Delta, payout.Reason);
        }
    }
}
