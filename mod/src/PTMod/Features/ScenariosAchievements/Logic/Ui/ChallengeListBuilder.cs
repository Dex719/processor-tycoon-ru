using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>One row of the window of the challenges: the title with its bonus, the description, whether it can be chosen and why not.</summary>
    public sealed class ChallengeRow
    {
        public string Id;

        /// <summary>The title with the bonus after it: <c>Expensive research (+15%)</c>.</summary>
        public string Caption;

        public string Description;

        /// <summary>The bonus of the challenge in whole per cent (15).</summary>
        public int BonusPercent;

        /// <summary>The challenge is in the choice of the player.</summary>
        public bool Selected;

        /// <summary>The challenge can be chosen for the present mode, company type and build; false: the toggle is inactive and <see cref="Reason"/> says why.</summary>
        public bool IsAvailable;

        /// <summary>The reason a challenge cannot be chosen, as text; null for an available one.</summary>
        public string Reason;
    }

    /// <summary>What the window of the challenges shows: the rows, how many are chosen, the total bonus and its line.</summary>
    public sealed class ChallengeListView
    {
        public readonly List<ChallengeRow> Rows = new List<ChallengeRow>();

        public int SelectedCount;

        /// <summary>The bonus of the chosen challenges in whole per cent; the bonuses add up (15 and 10 make 25).</summary>
        public int BonusPercent;

        /// <summary>"Score bonus: +25%", or "No challenges chosen".</summary>
        public string BonusText;

        /// <summary>The caption of the button "Challenges (N)..." of the new-game panel.</summary>
        public string ButtonCaption;
    }

    /// <summary>
    /// The model of the window of the challenges (TSK-022, FR-3 item 4, FR-11 item 3): the list of the catalog with the choice of the player, the reasons a
    /// challenge cannot be chosen (the company type, the list of the scenario, the build that cannot scale the player's default threshold) and the total bonus.
    /// The availability is the rule of <see cref="ChallengeRuntime.CheckSelectable"/>, the same one that takes a challenge off the choice when the mode changes.
    /// </summary>
    public static class ChallengeListBuilder
    {
        /// <param name="catalog">The challenges of the catalog, in the order to show them.</param>
        /// <param name="selection">The choice made so far (the scenario and the challenges).</param>
        /// <param name="companyType">The company type the start will have (<c>idm</c>, <c>fabless</c>, <c>foundry</c>); null when it is not known.</param>
        /// <param name="scenario">The chosen scenario; null for free play.</param>
        /// <param name="perCompanyCreditActive"><c>ModCredit.IsPerCompanyActive</c>.</param>
        public static ChallengeListView Build(IEnumerable<ChallengeDefinition> catalog, ScenarioSelection selection, string companyType, ScenarioDefinition scenario,
                                              bool perCompanyCreditActive, IUiTexts texts)
        {
            if (texts == null) throw new ArgumentNullException("texts");

            var view = new ChallengeListView();
            var chosen = new List<ChallengeDefinition>();
            if (catalog != null)
            {
                foreach (ChallengeDefinition challenge in catalog)
                {
                    if (challenge == null) continue;

                    ChallengeAvailability availability = ChallengeRuntime.CheckSelectable(challenge, companyType, scenario, perCompanyCreditActive);
                    bool selected = selection != null && selection.IsChallengeSelected(challenge.Id);
                    int bonus = ScoreCalculator.BonusPercent(new[] { challenge });

                    var row = new ChallengeRow
                    {
                        Id = challenge.Id,
                        Caption = texts.Text("chl.row", texts.Text(challenge.TitleKey), bonus),
                        Description = texts.Text(challenge.DescKey),
                        BonusPercent = bonus,
                        Selected = selected,
                        IsAvailable = availability.IsAvailable,
                        Reason = availability.IsAvailable ? null : texts.Text(availability.ReasonKey),
                    };
                    view.Rows.Add(row);
                    if (selected) chosen.Add(challenge);
                }
            }

            view.SelectedCount = chosen.Count;
            view.BonusPercent = ScoreCalculator.BonusPercent(chosen);
            view.BonusText = chosen.Count == 0 ? texts.Text("chl.none") : texts.Text("chl.bonus", view.BonusPercent);
            view.ButtonCaption = texts.Text("chl.button", chosen.Count);
            return view;
        }
    }
}
