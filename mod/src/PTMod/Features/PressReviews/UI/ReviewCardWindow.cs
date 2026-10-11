using System;
using System.Collections.Generic;
using PTMod.Core;
using TMPro;
using UnityEngine;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The card of a review (FR-4 item 2, NFR-5, design.md "Карточка обзора"): the metascore and the verdict in the colour of their tone, four axes
    /// (a number, a word and a bar each), the outlets with their marks and quotes, the reference CPUs by name, the effects in words and the notes. It shows
    /// no formulas. The window is not modal and does not stop the game; the colours follow the theme. What it shows is made by the pure
    /// <see cref="ReviewCard"/>; this class only puts it on the screen.
    /// <para>
    /// <b>The template (spike of TSK-011).</b> The window is made from the shared template <c>Window.Panel</c> (<c>ModUI.CreateWindow</c>, <see cref="WindowTemplate.Panel"/>).
    /// The object <c>ReviewWindow</c> of the scene <c>level1</c> was considered as the template and is not used: by the dump of the scene it carries an empty
    /// <c>ReviewWindow</c> script, <c>WindowHierarchyBehaviour</c>, <c>WindowStartBehaviour</c> and a <c>PauseTrigger</c>. It has no title bar, no close button and no
    /// <c>Themeable*</c> parts to give a frame, and the <c>PauseTrigger</c> would stop the game, which a card of a review must not do.
    /// </para>
    /// </summary>
    public static class ReviewCardWindow
    {
        private const string Feature = PressReviewsFeature.FeatureId;
        private const string WindowId = Feature + ".card";
        private const float BodyHeight = 540f;
        private const float BodyMinHeight = 200f;
        private const float HeadlineScale = 1.8f;
        private const float SmallScale = 0.9f;

        private static readonly Vector2 WindowSize = new Vector2(700f, 650f);

        private static ModWindow window;
        private static RectTransform body;

        /// <summary>The card is on the screen.</summary>
        public static bool IsOpen
        {
            get { return window != null && window.IsAlive && window.IsOpen; }
        }

        /// <summary>
        /// Opens the card of the review of a CPU (made on first use, filled anew each time). A CPU without a review in the archive gets a card that says so.
        /// Does nothing while the feature is not active or the templates of the scene are not ready. Never throws.
        /// </summary>
        public static void Open(int cpuSaveId)
        {
            ModHooks.Guard(Feature, "ui/card", () => OpenCore(cpuSaveId));
        }

        /// <summary>Closes the card when it is open (the feature was switched off).</summary>
        public static void Close()
        {
            if (window != null && window.IsAlive) window.Close();
        }

        private static void OpenCore(int cpuSaveId)
        {
            if (!ModFeatures.IsActive(Feature)) return;
            if (!ModUI.IsReady)
            {
                ModLog.Warn(Feature, "ui: the card cannot be made yet, the templates of the scene are not ready");
                return;
            }
            if (!EnsureWindow()) return;

            PressUi.Clear(body);
            body.anchoredPosition = Vector2.zero;   // a card starts at the top

            ReviewRecord record = ReviewArchive.Find(PressSaveAdapter.Instance.State, cpuSaveId);
            if (record == null)
            {
                window.SetTitle(ModL10n.L("press.card.titleMissing"));
                ModUI.Text(body, ModL10n.L("press.card.missing"));
            }
            else
            {
                Fill(ReviewCard.Build(record, PressUi.CardContext(record)));
            }
            window.Open();
        }

        private static bool EnsureWindow()
        {
            if (window != null && window.IsAlive && body != null) return true;

            window = null;
            body = null;
            ModWindow made = ModUI.CreateWindow(WindowId, WindowTemplate.Panel, "press.card.titleMissing", WindowSize);
            if (made == null) return false;   // ModUI has logged why

            PressUi.Clear(made.Content);   // the id is unique per scene: a window made before (a frame that failed halfway) comes back as it was
            RectTransform scroll = PressUi.ScrollBody(made.Content, BodyHeight, BodyMinHeight);
            if (scroll == null)
            {
                ModLog.Warn(Feature, "ui: the card has no scroll view; it stays closed");
                return false;
            }

            window = made;
            body = scroll;
            return true;
        }

        private static void Fill(ReviewCardModel model)
        {
            window.SetTitle(PressWords.Plain(model.Title));

            TMP_Text subtitle = ModUI.Text(body, model.Subtitle);
            Scale(subtitle, SmallScale);

            TMP_Text headline = ModUI.Text(body, model.Headline, true);
            Scale(headline, HeadlineScale);
            PressUi.Tint(headline, model.Tone);

            if (model.Compact)
            {
                ModUI.Text(body, model.CompactNote);
            }
            else
            {
                AddAxes(model);
                AddOutlets(model);
                if (!string.IsNullOrEmpty(model.Compared)) ModUI.Text(body, model.Compared);
            }

            AddSection(ModL10n.L("press.card.section.effects"), model.Effects, false);
            AddSection(ModL10n.L("press.card.section.notes"), model.Notes, true);
        }

        private static void AddAxes(ReviewCardModel model)
        {
            if (model.Axes.Count == 0) return;

            ModUI.Text(body, ModL10n.L("press.card.section.axes"), true);
            foreach (CardAxisRow axis in model.Axes)
            {
                if (ModUI.Info(body, axis.Name, axis.Value) == null) ModUI.Text(body, axis.Name + ": " + axis.Value);   // no info-text template in the scene
                PressUi.Bar(body, axis.Fraction, axis.Tone);
            }
        }

        private static void AddOutlets(ReviewCardModel model)
        {
            if (model.Outlets.Count == 0) return;

            ModUI.Text(body, ModL10n.L("press.card.section.outlets"), true);
            foreach (CardOutletRow outlet in model.Outlets)
            {
                string score = ModL10n.L("press.card.outletScore", outlet.Score);
                if (ModUI.Info(body, PressWords.Plain(outlet.Name), score) == null) ModUI.Text(body, outlet.Name + ": " + score);
                if (string.IsNullOrEmpty(outlet.Quote)) continue;

                TMP_Text quote = ModUI.Text(body, outlet.Quote);
                if (quote != null) quote.fontStyle = FontStyles.Italic;
            }
        }

        private static void AddSection(string heading, List<string> lines, bool italic)
        {
            if (lines == null || lines.Count == 0) return;

            ModUI.Text(body, heading, true);
            foreach (string line in lines)
            {
                TMP_Text text = ModUI.Text(body, line);
                if (italic && text != null) text.fontStyle = FontStyles.Italic;
            }
        }

        private static void Scale(TMP_Text text, float factor)
        {
            if (text != null) text.fontSize = text.fontSize * factor;
        }
    }
}
