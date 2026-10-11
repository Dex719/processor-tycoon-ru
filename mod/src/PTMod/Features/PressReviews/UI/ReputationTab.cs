using System;
using System.Collections.Generic;
using PTMod.Core;
using TMPro;
using UnityEngine;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The tab "Reputation" of the window "Press" (FR-7, AC-7.1): a table of the companies, the player first, each with the reputation and its level, the
    /// change over 12 months, the effect on popularity and the last three reasons. The change and the reasons are read from <c>ModReputation.History</c>
    /// (only the changes of <c>Add</c> and <c>Set</c>; the core's pull towards 50 is not in it), the reasons of the features are put into words
    /// (<see cref="ReputationReasons"/>); the effect is the one the feature really applies (<see cref="ReviewEffects.ReputationMultiplier"/>). The rows are
    /// made by the pure <see cref="ReputationTable"/>. A bankrupt AI company is not listed.
    /// </summary>
    internal static class ReputationTab
    {
        private const float SmallScale = 0.9f;

        /// <summary>Builds the tab into the emptied body of the window.</summary>
        internal static void Build(RectTransform body)
        {
            PressText words = PressUi.Words;
            var companies = new List<ReputationCompany>();
            var byKey = new Dictionary<string, ICompany>(StringComparer.Ordinal);
            CollectCompanies(companies, byKey);
            if (companies.Count == 0)
            {
                ModUI.Text(body, words("press.rep.empty"));
                return;
            }

            ModUI.Text(body, words("press.rep.intro"));

            ReviewEffects effects = PressReviewsFeature.Effects;
            ReviewConfig config = PressContent.Current.Config;
            List<ReputationRow> rows = ReputationTable.Build(
                companies,
                ModReputation.Get,
                ModReputation.History,
                key =>
                {
                    ICompany company;
                    if (effects != null && byKey.TryGetValue(key, out company)) return effects.ReputationMultiplier(company);
                    return EffectMath.ReputationEffect(ModReputation.Get(key), config.ReputationEffect);
                },
                ModTicks.Today,
                config,
                PressUi.ReviewedCpuName,
                words);

            foreach (ReputationRow row in rows) AddRow(body, row, words);
        }

        private static void CollectCompanies(List<ReputationCompany> companies, Dictionary<string, ICompany> byKey)
        {
            IReadOnlyList<ICompany> all = ModCompanies.All;
            for (int i = 0; i < all.Count; i++)
            {
                ICompany company = all[i];
                string key = ModCompanies.KeyOf(company);
                if (key == null) continue;

                bool player = key == ModCompanies.PlayerKey;
                if (!player && company.IsBankrupt) continue;

                companies.Add(new ReputationCompany { Key = key, Name = company.Name, IsPlayer = player });
                byKey[key] = company;
            }

            // The player first, whatever the order of the core's list is.
            companies.Sort((a, b) => a.IsPlayer == b.IsPlayer ? 0 : (a.IsPlayer ? -1 : 1));
        }

        private static void AddRow(RectTransform body, ReputationRow row, PressText words)
        {
            RectTransform box = ModUI.VerticalList(body, 2f) ?? body;

            string title = row.IsPlayer ? words("press.rep.row.you", row.CompanyName) : row.CompanyName;
            ModUI.Text(box, title, true);

            TMP_Text line = ModUI.Text(box, row.Line);
            PressUi.Tint(line, row.Change > 0.5f ? ScoreTone.Positive : (row.Change < -0.5f ? ScoreTone.Negative : ScoreTone.Neutral));

            string reasons = row.Reasons.Count == 0 ? words("press.rep.row.noReasons") : words("press.rep.row.reasons", string.Join("; ", row.Reasons.ToArray()));
            TMP_Text small = ModUI.Text(box, reasons);
            if (small != null) small.fontSize = small.fontSize * SmallScale;
        }
    }
}
