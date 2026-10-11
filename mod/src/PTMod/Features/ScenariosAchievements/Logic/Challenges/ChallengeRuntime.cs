using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>What happened to a challenge.</summary>
    public enum ChallengeEventKind
    {
        Failed,
        Passed,
    }

    /// <summary>
    /// A challenge failed or passed, for the notifications, the profile and the modifiers. A failure in free play carries the ids of the
    /// <c>ModModifiers</c> records to remove (<see cref="RemoveEntryIds"/>); a failure inside a scenario carries the order to lose the scenario
    /// (<see cref="LosesScenario"/>).
    /// </summary>
    public sealed class ChallengeEvent
    {
        public ChallengeEventKind Kind;

        public string ChallengeId;

        /// <summary>The fail rule that held (<see cref="ChallengeEventKind.Failed"/>).</summary>
        public string FailId;

        /// <summary>The L10n key of the reason (the <c>textKey</c> of the fail rule); null for a pass.</summary>
        public string TextKey;

        /// <summary>Records of <c>ModModifiers</c> to remove: the whole challenge goes when it fails in free play, otherwise empty.</summary>
        public string[] RemoveEntryIds = new string[0];

        /// <summary>The challenge failed inside a scenario, whose rules it adds to: the scenario is lost.</summary>
        public bool LosesScenario;

        /// <summary>Whole years played when the challenge was passed (<see cref="ChallengeEventKind.Passed"/>): what the profile records.</summary>
        public int Years;

        public DateTime Date;

        /// <summary>The internal event of the feature that this is (<c>challenge.failed</c> or <c>challenge.passed</c>): what the director records for the achievements.</summary>
        public string EventId
        {
            get { return Kind == ChallengeEventKind.Failed ? "challenge.failed" : "challenge.passed"; }
        }
    }

    /// <summary>Whether a challenge may be chosen for a game, and if not, the L10n key of the reason.</summary>
    public sealed class ChallengeAvailability
    {
        public bool IsAvailable;

        /// <summary>The key of the reason it cannot be chosen (<c>chl.reason.company</c>, <c>chl.reason.scenario</c>, <c>chl.&lt;id&gt;.unavailable</c>); null when it can.</summary>
        public string ReasonKey;

        public static ChallengeAvailability Available()
        {
            return new ChallengeAvailability { IsAvailable = true };
        }

        public static ChallengeAvailability Unavailable(string reasonKey)
        {
            return new ChallengeAvailability { IsAvailable = false, ReasonKey = reasonKey };
        }
    }

    /// <summary>
    /// The course of the challenges of a run (design.md, "Испытания"; FR-11 items 2-5): each day it looks at the fail rules of every active
    /// challenge, fails the one whose rule holds, and in free play passes a challenge that went <c>challengeYears</c> years (or reached the end year
    /// of the game, 2030) without failing. A failure in free play hands out the ids of its modifier records to remove and the game goes on; inside a
    /// scenario the failure loses the scenario. Also tells whether a challenge may be chosen at all (company type, <c>allowedChallenges</c>,
    /// the per-company credit that "Жёсткий кредит" needs). Pure: the days and the metrics come in, the events go out.
    /// </summary>
    public sealed class ChallengeRuntime
    {
        /// <summary><c>GameDefines.EndYear</c>: the game ends in this year, so a challenge in free play is passed by then at the latest.</summary>
        public const int EndYear = 2030;

        /// <summary>The length of a challenge when the config does not say (<c>challengeYears</c>).</summary>
        public const int DefaultYears = 10;

        public const string ReasonCompany = "chl.reason.company";
        public const string ReasonScenario = "chl.reason.scenario";

        private readonly FailEvaluator _fails;

        public ChallengeRuntime() : this(new FailEvaluator())
        {
        }

        public ChallengeRuntime(FailEvaluator fails)
        {
            _fails = fails ?? new FailEvaluator();
        }

        /// <summary>
        /// The pass of a game day over the challenges of the run. <paramref name="active"/> are their definitions, <paramref name="st"/> their states;
        /// a challenge that is not active (failed or passed already) is skipped. <paramref name="freePlay"/>: there is no scenario in the run.
        /// Returns what happened, in the order of <paramref name="active"/>.
        /// </summary>
        public IList<ChallengeEvent> EvaluateDay(IList<ChallengeDefinition> active, ChallengeStates st, MetricsSnapshot m, bool freePlay,
                                                 int challengeYears, DateTime today, IConditionAdapter conditions = null, EventBook events = null)
        {
            var list = new List<ChallengeEvent>();
            if (active == null || active.Count == 0) return list;
            if (st == null) throw new ArgumentNullException("st");
            if (challengeYears < 1) challengeYears = DefaultYears;

            var ctx = new CheckContext { Metrics = m, Today = today, Conditions = conditions, Events = events };
            for (int i = 0; i < active.Count; i++)
            {
                ChallengeDefinition challenge = active[i];
                if (challenge == null) continue;
                ChallengeState state = st.Get(challenge.Id);
                if (state == null || state.Status != ChallengeStatus.Active) continue;

                FailSpec triggered = _fails.FirstTriggered(challenge.Fail, state.Fail, ctx);
                if (triggered != null)
                {
                    state.Status = ChallengeStatus.Failed;
                    state.FailedOn = today.Date;

                    string[] remove = freePlay ? state.EntryIds.ToArray() : new string[0];
                    if (freePlay) state.EntryIds.Clear();

                    list.Add(new ChallengeEvent
                    {
                        Kind = ChallengeEventKind.Failed,
                        ChallengeId = challenge.Id,
                        FailId = triggered.Id,
                        TextKey = triggered.TextKey,
                        RemoveEntryIds = remove,
                        LosesScenario = !freePlay,
                        Date = today.Date,
                    });
                    continue;
                }

                if (freePlay && IsFinished(st.StartDate, today, challengeYears))
                {
                    state.Status = ChallengeStatus.Passed;
                    state.PassedOn = today.Date;
                    list.Add(new ChallengeEvent
                    {
                        Kind = ChallengeEventKind.Passed,
                        ChallengeId = challenge.Id,
                        Years = YearsBetween(st.StartDate, today),
                        Date = today.Date,
                    });
                }
            }
            return list;
        }

        /// <summary>
        /// Whether a challenge may be chosen for a game of the given company type: <c>scenario</c> is the chosen scenario or null for free play,
        /// <paramref name="perCompanyCreditActive"/> is <c>ModCredit.IsPerCompanyActive</c>. The reasons are checked in this order: the type of
        /// the company, the list of the scenario, the credit.
        /// </summary>
        public static ChallengeAvailability CheckSelectable(ChallengeDefinition challenge, string companyType, ScenarioDefinition scenario,
                                                            bool perCompanyCreditActive)
        {
            if (challenge == null) throw new ArgumentNullException("challenge");

            string[] types = challenge.RequiresCompanyType;
            if (types != null && types.Length > 0 && !Contains(types, companyType)) return ChallengeAvailability.Unavailable(ReasonCompany);

            if (scenario != null && !Contains(scenario.AllowedChallenges, challenge.Id)) return ChallengeAvailability.Unavailable(ReasonScenario);

            if (challenge.RequiresPerCompanyCredit && !perCompanyCreditActive) return ChallengeAvailability.Unavailable(challenge.UnavailableKey);

            return ChallengeAvailability.Available();
        }

        /// <summary>Whole years from <paramref name="start"/> to <paramref name="today"/> (a year is full on the same date a year later).</summary>
        public static int YearsBetween(DateTime start, DateTime today)
        {
            if (today.Date <= start.Date) return 0;
            int years = today.Year - start.Year;
            if (today.Date < AddYears(start, years)) years--;
            return Math.Max(0, years);
        }

        // The challenge is over: the years have gone by, or the game has reached its last year.
        private static bool IsFinished(DateTime start, DateTime today, int challengeYears)
        {
            return today.Year >= EndYear || today.Date >= AddYears(start, challengeYears);
        }

        // 29 February plus a year is 28 February, as DateTime does it; a date near the end of the calendar does not throw.
        private static DateTime AddYears(DateTime date, int years)
        {
            try
            {
                return date.Date.AddYears(years);
            }
            catch (ArgumentOutOfRangeException)
            {
                return DateTime.MaxValue;
            }
        }

        private static bool Contains(string[] list, string value)
        {
            if (list == null || value == null) return false;
            for (int i = 0; i < list.Length; i++)
            {
                if (string.Equals(list[i], value, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
