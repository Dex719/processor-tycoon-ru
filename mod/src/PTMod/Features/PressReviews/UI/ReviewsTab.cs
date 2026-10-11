using System;
using System.Collections.Generic;
using PTMod.Core;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The tab "Reviews" of the window "Press" (FR-9, AC-9.1): the archive of reviews, the newest on top, three filters (company, segment, year; each is a
    /// dropdown list), pages of 50 rows with the buttons Back and Forward. A click on a row opens the card of the review. The list is made by the pure
    /// <see cref="ReviewList"/>; the filters and the page are kept while the window lives (they start from "all" and the first page).
    /// </summary>
    internal static class ReviewsTab
    {
        private const string Feature = PressReviewsFeature.FeatureId;
        private const float FilterRowHeight = 34f;
        private const float PagerButtonWidth = 110f;

        // The tab stops of the columns of a row, in per cent of its width: date, company, CPU, segment, score, verdict.
        private static readonly float[] ColumnStops = { 0f, 11f, 28f, 55f, 70f, 78f };

        private static readonly ReviewFilter Filter = new ReviewFilter();
        private static int page;

        /// <summary>Builds the tab into the emptied body of the window.</summary>
        internal static void Build(RectTransform body)
        {
            PressText words = PressUi.Words;
            IList<ReviewRecord> archive = PressSaveAdapter.Instance.State.reviews;
            if (archive == null || archive.Count == 0)
            {
                ModUI.Text(body, words("press.reviews.empty"));
                return;
            }

            List<CompanyOption> companies = ReviewList.Companies(archive);
            List<int> years = ReviewList.Years(archive);
            DropMissingFilters(companies, years);

            BuildFilters(body, companies, years);

            List<ReviewRecord> found = ReviewList.Select(archive, Filter);
            page = ReviewList.ClampPage(page, found.Count);
            BuildPager(body, found.Count);

            if (found.Count == 0)
            {
                ModUI.Text(body, words("press.reviews.noMatch"));
                return;
            }

            ModUI.Text(body, PressUi.Columns(HeaderCells(words), ColumnStops), true);
            RectTransform list = ModUI.VerticalList(body, 2f) ?? body;
            foreach (ReviewRecord record in ReviewList.Page(found, page)) AddRow(list, record, words);
        }

        // ---- filters ----

        private static void DropMissingFilters(List<CompanyOption> companies, List<int> years)
        {
            // A filter that names a company or a year the archive no longer has (the archive was reloaded) goes back to "all".
            if (Filter.CompanyKey != null && !companies.Exists(c => string.Equals(c.Key, Filter.CompanyKey, StringComparison.Ordinal))) Filter.CompanyKey = null;
            if (Filter.Year > 0 && !years.Contains(Filter.Year)) Filter.Year = 0;
        }

        private static void BuildFilters(RectTransform body, List<CompanyOption> companies, List<int> years)
        {
            PressText words = PressUi.Words;
            RectTransform row = PressUi.Row(body, "PTPressFilters", FilterRowHeight, 10f);

            var companyNames = new List<string> { words("press.filter.all") };
            int companyIndex = 0;
            for (int i = 0; i < companies.Count; i++)
            {
                companyNames.Add(PressWords.Plain(companies[i].Name));
                if (string.Equals(companies[i].Key, Filter.CompanyKey, StringComparison.Ordinal)) companyIndex = i + 1;
            }
            ModUI.Dropdown(row, words("press.reviews.filter.company"), companyNames, companyIndex, index =>
            {
                Filter.CompanyKey = index <= 0 || index > companies.Count ? null : companies[index - 1].Key;
                Changed();
            });

            var segmentNames = new List<string> { words("press.filter.all") };
            for (int segment = 0; segment < 4; segment++) segmentNames.Add(words(PressWords.SegmentKey(segment)));
            ModUI.Dropdown(row, words("press.reviews.filter.segment"), segmentNames, Filter.Segment + 1, index =>
            {
                Filter.Segment = index <= 0 ? -1 : index - 1;
                Changed();
            });

            var yearNames = new List<string> { words("press.filter.all") };
            int yearIndex = 0;
            for (int i = 0; i < years.Count; i++)
            {
                yearNames.Add(years[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (years[i] == Filter.Year) yearIndex = i + 1;
            }
            ModUI.Dropdown(row, words("press.reviews.filter.year"), yearNames, yearIndex, index =>
            {
                Filter.Year = index <= 0 || index > years.Count ? 0 : years[index - 1];
                Changed();
            });
        }

        private static void Changed()
        {
            page = 0;   // another set of reviews starts on its first page
            PressWindow.Refresh();
        }

        // ---- pages ----

        private static void BuildPager(RectTransform body, int count)
        {
            PressText words = PressUi.Words;
            int pages = ReviewList.PageCount(count);

            RectTransform row = PressUi.Row(body, "PTPressPager", FilterRowHeight, 10f);
            ButtonUI back = ModUI.Button(row, words("press.reviews.prev"), () => Turn(-1));
            if (back != null)
            {
                PressUi.SetWidth(back, PagerButtonWidth, 0f);
                back.Interactable = page > 0;
            }

            ModUI.Text(row, words("press.reviews.page", page + 1, pages, count));

            ButtonUI next = ModUI.Button(row, words("press.reviews.next"), () => Turn(1));
            if (next != null)
            {
                PressUi.SetWidth(next, PagerButtonWidth, 0f);
                next.Interactable = page < pages - 1;
            }
        }

        private static void Turn(int step)
        {
            page = Math.Max(0, page + step);   // the build holds it to the last page
            PressWindow.Refresh();
        }

        // ---- rows ----

        private static string[] HeaderCells(PressText words)
        {
            return new[]
            {
                words("press.reviews.col.date"), words("press.reviews.col.company"), words("press.reviews.col.cpu"),
                words("press.reviews.col.segment"), words("press.reviews.col.score"), words("press.reviews.col.verdict"),
            };
        }

        private static void AddRow(RectTransform list, ReviewRecord record, PressText words)
        {
            ReviewListRow row = ReviewList.Row(record, words);
            string[] cells = (string[])row.Columns.Clone();
            cells[4] = PressUi.Colored(cells[4], row.Tone);
            cells[5] = PressUi.Colored(cells[5], row.Tone);

            int cpuSaveId = row.CpuSaveId;
            ButtonUI button = ModUI.Button(list, PressUi.Columns(cells, ColumnStops), () => ReviewCardWindow.Open(cpuSaveId),
                ButtonStyle.Standard, words("press.reviews.row.tip"));
            if (button == null)
            {
                ModLog.Warn(Feature, "ui: a row of the list could not be made (the button template is missing)");
                return;
            }
            PressUi.StyleRowButton(button);
        }
    }
}
