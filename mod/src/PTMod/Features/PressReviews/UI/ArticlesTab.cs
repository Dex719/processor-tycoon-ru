using System.Collections.Generic;
using PTMod.Core;
using TMPro;
using UnityEngine;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The tab "Articles" of the window "Press" (FR-11 item 3, design.md): the articles other features published through the API of the feature (the last
    /// 200), the newest first, each with its date, its title, its text and its source. It reads <c>PressState.articles</c>; the lines are made by the pure
    /// <see cref="PressTabViews.Articles"/>.
    /// </summary>
    internal static class ArticlesTab
    {
        private const float SmallScale = 0.9f;

        /// <summary>Builds the tab into the emptied body of the window.</summary>
        internal static void Build(RectTransform body)
        {
            PressText words = PressUi.Words;
            List<ArticleView> articles = PressTabViews.Articles(PressSaveAdapter.Instance.State.articles, SourceNameOf, words);
            if (articles.Count == 0)
            {
                ModUI.Text(body, words("press.articles.empty"));
                return;
            }

            foreach (ArticleView article in articles)
            {
                RectTransform box = ModUI.VerticalList(body, 2f) ?? body;
                ModUI.Text(box, words("press.articles.heading", article.Date, PressWords.Plain(article.Title)), true);
                if (!string.IsNullOrEmpty(article.Body)) ModUI.Text(box, article.Body);
                if (string.IsNullOrEmpty(article.Source)) continue;

                TMP_Text source = ModUI.Text(box, article.Source);
                if (source != null) source.fontSize = source.fontSize * SmallScale;
            }
        }

        /// <summary>The name of the feature that wrote an article, as the hub shows it; null when the id is not a feature of the mod.</summary>
        private static string SourceNameOf(string featureId)
        {
            FeatureInfo info = ModFeatures.Find(featureId);
            if (info == null || string.IsNullOrEmpty(info.NameKey) || !ModL10n.Has(info.NameKey)) return null;
            return ModL10n.L(info.NameKey);
        }
    }
}
