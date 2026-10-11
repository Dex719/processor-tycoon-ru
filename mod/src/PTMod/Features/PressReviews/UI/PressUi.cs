using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using PTMod.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using Cpu = ProcessorTycoon.Hardware.Cpu;
using CpuDataProvider = ProcessorTycoon.Hardware.CpuDataProvider;
using GameColors = ProcessorTycoon.Colors;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The small tools the windows and the letter of press-reviews share (not for other features): the strings, the colours of the tones of a mark and their
    /// repainting when the theme changes, a horizontal row, the bar of an axis, the deferred rebuild of a window (a widget is not destroyed inside its own
    /// click), the lookups of the game the pure builders ask for (an outlet's name, a CPU's name, the reputation step of a review) and the context of a card.
    /// Everything is made from <see cref="ModUI"/> and the plain Unity UI; main thread only.
    /// </summary>
    internal static class PressUi
    {
        internal const string Feature = PressReviewsFeature.FeatureId;

        private const float BarHeight = 8f;

        /// <summary>The strings of the feature in the current language (<c>ModL10n.L</c>).</summary>
        internal static readonly PressText Words = ModL10n.L;

        // The colours of the tones when the game's own are not at hand (the main menu has no theme manager): green, red and amber.
        private static readonly Color FallbackPositive = new Color(0.30f, 0.72f, 0.38f, 1f);
        private static readonly Color FallbackNegative = new Color(0.86f, 0.32f, 0.30f, 1f);
        private static readonly Color FallbackWarning = new Color(0.92f, 0.68f, 0.22f, 1f);
        private static readonly Color BarTrack = new Color(0.5f, 0.5f, 0.5f, 0.28f);

        private sealed class Tinted
        {
            public TMP_Text Text;
            public Graphic Graphic;
            public ScoreTone Tone;
        }

        private static readonly List<Tinted> TintedItems = new List<Tinted>();
        private static bool themeHooked;
        private static int pruneAt = 64;

        // ---- colours and the theme ----

        /// <summary>The colour of a tone in the current theme: the game's positive and negative colours, the warning colour for the neutral tone of a bar.</summary>
        internal static Color ColorOf(ScoreTone tone)
        {
            try
            {
                Color color;
                if (TryGameColor(tone, out color)) return color;
            }
            catch (Exception)
            {
                // the game's Colors or ThemeManager are not what the mod expects (or not there yet): the fallback colours below
            }

            switch (tone)
            {
                case ScoreTone.Positive: return FallbackPositive;
                case ScoreTone.Negative: return FallbackNegative;
                default: return FallbackWarning;
            }
        }

        // The game's types are touched only here; the caller catches what a mismatch throws.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryGameColor(ScoreTone tone, out Color color)
        {
            GameColors colors = GameColors.Instance;
            if (colors == null)
            {
                color = default(Color);
                return false;
            }

            switch (tone)
            {
                case ScoreTone.Positive: color = colors.DynamicPositiveColor(); break;
                case ScoreTone.Negative: color = colors.DynamicNegativeColor(); break;
                default: color = colors.DynamicWarningColor(); break;
            }
            return true;
        }

        /// <summary>Paints a text with the colour of its tone and keeps it in step with the theme. A neutral text keeps the colour <see cref="ModUI"/> gave it.</summary>
        internal static void Tint(TMP_Text text, ScoreTone tone)
        {
            if (text == null || tone == ScoreTone.Neutral) return;

            HookTheme();
            text.color = ColorOf(tone);
            TintedItems.Add(new Tinted { Text = text, Tone = tone });
        }

        private static void TintGraphic(Graphic graphic, ScoreTone tone)
        {
            if (graphic == null) return;

            HookTheme();
            graphic.color = ColorOf(tone);
            TintedItems.Add(new Tinted { Graphic = graphic, Tone = tone });
        }

        private static void HookTheme()
        {
            if (TintedItems.Count >= pruneAt)
            {
                TintedItems.RemoveAll(item => (item.Text == null) && (item.Graphic == null));
                pruneAt = Math.Max(64, TintedItems.Count * 2);
            }

            if (themeHooked) return;
            themeHooked = true;
            ModUI.ThemeChanged += OnThemeChanged;
        }

        /// <summary>The theme changed: <see cref="ModUI"/> repainted its texts first, now the tones are painted again.</summary>
        private static void OnThemeChanged()
        {
            TintedItems.RemoveAll(item => (item.Text == null) && (item.Graphic == null));
            foreach (Tinted item in TintedItems)
            {
                Color color = ColorOf(item.Tone);
                if (item.Text != null) item.Text.color = color;
                if (item.Graphic != null) item.Graphic.color = color;
            }
        }

        // ---- layout ----

        /// <summary>Empties a container: the children leave the layout at once and are destroyed at the end of the frame.</summary>
        internal static void Clear(Transform parent)
        {
            if (parent == null) return;

            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }

        /// <summary>A horizontal row at full width (children side by side, left to right) under the parent; <paramref name="height"/> 0 lets the children decide.</summary>
        internal static RectTransform Row(Transform parent, string name, float height, float spacing)
        {
            var row = new GameObject(name, typeof(RectTransform));
            try
            {
                row.layer = parent.gameObject.layer;
                var group = row.AddComponent<HorizontalLayoutGroup>();
                group.spacing = spacing;
                group.childAlignment = TextAnchor.MiddleLeft;
                group.childControlWidth = true;
                group.childControlHeight = true;
                group.childForceExpandWidth = false;
                group.childForceExpandHeight = false;

                var layout = row.AddComponent<LayoutElement>();
                layout.flexibleWidth = 1f;
                if (height > 0f)
                {
                    layout.minHeight = height;
                    layout.preferredHeight = height;
                }

                row.transform.SetParent(parent, false);
                return (RectTransform)row.transform;
            }
            catch (Exception)
            {
                UnityEngine.Object.DestroyImmediate(row);
                throw;
            }
        }

        /// <summary>Gives a widget in a horizontal row a fixed width (<paramref name="flexible"/> 0) or a share of the free width.</summary>
        internal static void SetWidth(Component widget, float preferred, float flexible)
        {
            if (widget == null) return;

            LayoutElement layout = widget.GetComponent<LayoutElement>();
            if (layout == null) layout = widget.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = preferred;
            layout.flexibleWidth = flexible;
        }

        /// <summary>
        /// A scrolling body under the window's content that takes the height the window has left (the nominal height is the starting point), like the body of the
        /// hub. Null when the scroll view could not be made.
        /// </summary>
        internal static RectTransform ScrollBody(RectTransform content, float height, float minHeight)
        {
            RectTransform body = ModUI.ScrollView(content, height);
            if (body == null) return null;

            ScrollRect scroll = body.GetComponentInParent<ScrollRect>();
            LayoutElement element = scroll == null ? null : scroll.GetComponent<LayoutElement>();
            if (element != null)
            {
                element.minHeight = minHeight;
                element.preferredHeight = height;
                element.flexibleHeight = 1f;
            }
            return body;
        }

        /// <summary>A bar of an axis: a track and a fill of the given share, painted by the tone and repainted when the theme changes.</summary>
        internal static void Bar(Transform parent, float fraction, ScoreTone tone)
        {
            if (parent == null) return;

            var root = new GameObject("PTPressBar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            try
            {
                root.layer = parent.gameObject.layer;
                var track = root.GetComponent<Image>();
                track.color = BarTrack;
                track.raycastTarget = false;
                var layout = root.GetComponent<LayoutElement>();
                layout.minHeight = BarHeight;
                layout.preferredHeight = BarHeight;
                layout.flexibleWidth = 1f;

                var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
                fill.layer = root.layer;
                var rect = (RectTransform)fill.transform;
                rect.SetParent(root.transform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                var image = fill.GetComponent<Image>();
                image.raycastTarget = false;
                TintGraphic(image, tone);

                root.transform.SetParent(parent, false);
            }
            catch (Exception ex)
            {
                UnityEngine.Object.DestroyImmediate(root);
                ModLog.Error(Feature, "ui: cannot make a bar", ex);
            }
        }

        // ---- rows of text ----

        /// <summary>Cells of a row joined with TextMeshPro tab stops (<c>&lt;pos=…%&gt;</c>), so the columns line up in a proportional font.</summary>
        internal static string Columns(string[] cells, float[] positions)
        {
            var text = new StringBuilder();
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) text.Append("<pos=").Append(positions[i].ToString("0.#", CultureInfo.InvariantCulture)).Append("%>");
                text.Append(cells[i] ?? string.Empty);
            }
            return text.ToString();
        }

        /// <summary>The cell wrapped in the colour of its tone (inline, so a row needs no component per cell).</summary>
        internal static string Colored(string cell, ScoreTone tone)
        {
            if (tone == ScoreTone.Neutral || string.IsNullOrEmpty(cell)) return cell;

            Color color = ColorOf(tone);
            return "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + cell + "</color>";
        }

        /// <summary>Makes the caption of a button a single left-aligned line that is cut with dots when it is too long (a row of a list).</summary>
        internal static void StyleRowButton(ButtonUI button)
        {
            if (button == null) return;

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label == null) return;

            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.richText = true;
        }

        // ---- time ----

        /// <summary>Runs the action one frame later under the guard of the feature, so a widget is not destroyed inside its own click handler.</summary>
        internal static void Defer(Action action)
        {
            if (action == null) return;

            PTModHost host = PTModHost.Instance;
            if (host != null && host.Run(OneFrameLater(action)) != null) return;
            ModHooks.Guard(Feature, "ui/deferred", action);
        }

        private static IEnumerator OneFrameLater(Action action)
        {
            yield return null;
            ModHooks.Guard(Feature, "ui/deferred", action);
        }

        // ---- what the pure builders ask the game ----

        /// <summary>The name of an outlet: its name in the content (the key of the table wins over the inline text), the table's key for an outlet the content lost, the id.</summary>
        internal static string OutletNameOf(string outletId)
        {
            if (string.IsNullOrEmpty(outletId)) return string.Empty;

            IReadOnlyList<Outlet> outlets = PressContent.Current.Outlets;
            for (int i = 0; i < outlets.Count; i++)
            {
                if (string.Equals(outlets[i].Id, outletId, StringComparison.Ordinal)) return ModL10n.Resolve(outlets[i].Name);
            }

            string key = "press.outlet." + outletId;
            return ModL10n.Has(key) ? ModL10n.L(key) : outletId;
        }

        /// <summary>A CPU of the game as <c>name (company)</c>; null when the game no longer has it.</summary>
        internal static string CpuLabelOf(int cpuSaveId)
        {
            Cpu cpu = FindCpu(cpuSaveId);
            if (cpu == null) return null;

            string company = cpu.Company == null ? string.Empty : cpu.Company.Name;
            return string.IsNullOrEmpty(company) ? PressWords.Plain(cpu.Name) : PressWords.Plain(cpu.Name) + " (" + PressWords.Plain(company) + ")";
        }

        private static Cpu FindCpu(int cpuSaveId)
        {
            CpuDataProvider provider = CpuDataProvider.Instance;
            return provider == null ? null : provider.GetCpu(cpuSaveId);
        }

        /// <summary>The name of a reviewed CPU from the archive (it stays there when the game forgets the CPU); null when there is no such review.</summary>
        internal static string ReviewedCpuName(int cpuSaveId)
        {
            ReviewRecord record = ReviewArchive.Find(PressSaveAdapter.Instance.State, cpuSaveId);
            return record == null ? null : record.cpuName;
        }

        /// <summary>The reviewed CPU as <c>Company CPU</c> for the awards; null when the archive has no such review.</summary>
        internal static string ReviewedCpuLabel(int cpuSaveId)
        {
            ReviewRecord record = ReviewArchive.Find(PressSaveAdapter.Instance.State, cpuSaveId);
            if (record == null) return null;

            string company = PressWords.Plain(record.companyName).Trim();
            string cpu = PressWords.Plain(record.cpuName).Trim();
            return company.Length == 0 ? cpu : company + " " + cpu;
        }

        /// <summary>
        /// The name of a company by its key: the company of the game, otherwise the name it was reviewed under in the archive, otherwise the key itself.
        /// </summary>
        internal static string CompanyNameOf(string companyKey)
        {
            ICompany company = ModCompanies.Resolve(companyKey);
            if (company != null && !string.IsNullOrEmpty(company.Name)) return company.Name;

            List<ReviewRecord> reviews = PressSaveAdapter.Instance.State.reviews;
            for (int i = reviews.Count - 1; i >= 0; i--)
            {
                if (string.Equals(reviews[i].companyKey, companyKey, StringComparison.Ordinal) && !string.IsNullOrEmpty(reviews[i].companyName)) return reviews[i].companyName;
            }
            return companyKey;
        }

        /// <summary>The names the awards (the tab and the letter) are written with.</summary>
        internal static PressTabViews.AwardsLookups AwardsLookups()
        {
            return new PressTabViews.AwardsLookups { CompanyName = CompanyNameOf, ReviewedCpu = ReviewedCpuLabel };
        }

        /// <summary>The context a card or a letter about a review is built with: the strings, the names of the outlets and CPUs, the reputation step the review made.</summary>
        internal static ReviewCardContext CardContext(ReviewRecord record)
        {
            ReputationStep step = null;
            if (record != null && !string.IsNullOrEmpty(record.companyKey))
            {
                step = ReputationTable.StepOf(ModReputation.History(record.companyKey), EffectMath.ReviewReason(record.cpuSaveId));
            }

            return new ReviewCardContext
            {
                Text = Words,
                OutletName = OutletNameOf,
                PeerName = CpuLabelOf,
                Reputation = step,
                Config = PressContent.Current.Config,
            };
        }
    }
}
