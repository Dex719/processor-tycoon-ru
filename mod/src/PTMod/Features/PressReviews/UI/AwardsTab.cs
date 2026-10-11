using System;
using System.Collections.Generic;
using PTMod.Core;
using UnityEngine;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The tab "Awards of the year" of the window "Press" (FR-8, design.md): the years of the awards, the latest first; for each year the "CPU of the Year"
    /// of every segment, the "Company of the Year" and the "Disappointment of the Year". It reads <c>PressState.awards</c> (the awards are made on 1 January by
    /// <c>AwardsService</c>). The lines are made by the pure <see cref="PressTabViews.Awards"/>.
    /// </summary>
    internal static class AwardsTab
    {
        /// <summary>Builds the tab into the emptied body of the window.</summary>
        internal static void Build(RectTransform body)
        {
            PressText words = PressUi.Words;

            List<AwardsYearView> years = PressTabViews.Awards(PressSaveAdapter.Instance.State.awards, PressUi.AwardsLookups(), words);
            if (years.Count == 0)
            {
                ModUI.Text(body, words("press.awards.empty"));
                return;
            }

            foreach (AwardsYearView year in years)
            {
                RectTransform box = ModUI.VerticalList(body, 2f) ?? body;
                ModUI.Text(box, year.Heading, true);
                if (year.Lines.Count == 0) ModUI.Text(box, words("press.awards.yearEmpty"));
                foreach (string line in year.Lines) ModUI.Text(box, line);
            }
        }
    }
}
