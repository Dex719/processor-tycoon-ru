using System;
using System.Collections.Generic;
using PTMod.Core;
using TMPro;
using GameColors = ProcessorTycoon.Colors;
using GameResearchButton = ProcessorTycoon.ResearchSystem.ResearchButton;
using UnityColorUtility = UnityEngine.ColorUtility;
using UnityLayoutElement = UnityEngine.UI.LayoutElement;
using UnityObject = UnityEngine.Object;
using UnityRectTransform = UnityEngine.RectTransform;
using UnityVector2 = UnityEngine.Vector2;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The mark "NEW" on the buttons of the technologies that became available and have not been looked at (FR-3 item 1): a small text in the
    /// top right corner of the button of the game, in the font of the game (<c>ModUI.Text</c>) and in the warning colour of the present theme.
    /// A label is made the first time its technology is new and is only switched on and off after that, so the cost of the marks follows the
    /// number of new technologies, not the 97 buttons. The label sits outside any layout of the button, takes no clicks and is destroyed with
    /// <see cref="Destroy"/> (the feature switched off). Which technologies are new is the answer of <see cref="NewTechTracker"/>; this class
    /// only draws it.
    /// </summary>
    internal sealed class NewBadges
    {
        private const string LabelName = "PTModNewBadge";
        private const float LabelWidth = 96f;
        private const float LabelHeight = 20f;
        private const float MarginRight = 6f;
        private const float MarginTop = 4f;

        private readonly TreeIndex _tree;
        private readonly Dictionary<int, TMP_Text> _labels = new Dictionary<int, TMP_Text>();
        private readonly HashSet<int> _shown = new HashSet<int>();
        private readonly HashSet<int> _wanted = new HashSet<int>();
        private readonly List<int> _toHide = new List<int>();

        private string _caption;
        private bool _unavailable;

        public NewBadges(TreeIndex tree)
        {
            _tree = tree;
            _caption = BuildCaption();
        }

        /// <summary>How many labels are on now.</summary>
        public int ShownCount
        {
            get { return _shown.Count; }
        }

        /// <summary>True when the button of the technology carries a label that is on.</summary>
        public bool IsShown(int technologyId)
        {
            return _shown.Contains(technologyId);
        }

        /// <summary>Puts the labels on exactly the new technologies in <paramref name="unseen"/> and takes them off the others.</summary>
        public void Apply(IReadOnlyList<TechSnapshot> unseen)
        {
            _wanted.Clear();
            if (unseen != null)
            {
                for (int i = 0; i < unseen.Count; i++)
                {
                    if (unseen[i] != null) _wanted.Add(unseen[i].Id);
                }
            }

            _toHide.Clear();
            foreach (int id in _shown)
            {
                if (!_wanted.Contains(id)) _toHide.Add(id);
            }
            foreach (int id in _toHide)
            {
                _shown.Remove(id);
                TMP_Text label;
                if (_labels.TryGetValue(id, out label) && label != null) label.gameObject.SetActive(false);
            }

            foreach (int id in _wanted)
            {
                if (_shown.Contains(id)) continue;
                TMP_Text label = LabelOf(id);
                if (label == null) continue;
                label.gameObject.SetActive(true);
                _shown.Add(id);
            }
        }

        /// <summary>Writes the caption again (the language or the theme changed) on every label made so far.</summary>
        public void RefreshStyle()
        {
            _caption = BuildCaption();
            foreach (KeyValuePair<int, TMP_Text> pair in _labels)
            {
                if (pair.Value != null) pair.Value.text = _caption;
            }
        }

        /// <summary>Removes every label from the buttons (the feature is switched off, FR-8 item 3).</summary>
        public void Destroy()
        {
            foreach (KeyValuePair<int, TMP_Text> pair in _labels)
            {
                if (pair.Value != null) UnityObject.Destroy(pair.Value.gameObject);
            }
            _labels.Clear();
            _shown.Clear();
        }

        // ---- labels ----

        private TMP_Text LabelOf(int technologyId)
        {
            TMP_Text label;
            if (_labels.TryGetValue(technologyId, out label) && label != null) return label;
            if (_unavailable) return null;

            GameResearchButton button = _tree.ButtonOf(technologyId);
            if (button == null) return null;

            label = Create(button);
            if (label == null)
            {
                _unavailable = true;   // the template of the text is missing: one warning, no retry on every day
                ModLog.Warn(ResearchTreeUxFeature.FeatureId, "badges: the label \"new\" cannot be made, the buttons stay without it");
                return null;
            }

            _labels[technologyId] = label;
            return label;
        }

        private TMP_Text Create(GameResearchButton button)
        {
            TMP_Text label = ModUI.Text(button.transform, _caption, true);
            if (label == null) return null;

            label.gameObject.name = LabelName;
            label.alignment = TextAlignmentOptions.TopRight;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.richText = true;
            label.raycastTarget = false;

            // Outside the layout of the button, in its top right corner.
            UnityLayoutElement layout = label.gameObject.GetComponent<UnityLayoutElement>();
            if (layout == null) layout = label.gameObject.AddComponent<UnityLayoutElement>();
            layout.ignoreLayout = true;

            UnityRectTransform rect = label.rectTransform;
            rect.anchorMin = new UnityVector2(1f, 1f);
            rect.anchorMax = new UnityVector2(1f, 1f);
            rect.pivot = new UnityVector2(1f, 1f);
            rect.sizeDelta = new UnityVector2(LabelWidth, LabelHeight);
            rect.anchoredPosition = new UnityVector2(-MarginRight, -MarginTop);
            return label;
        }

        // The text of the key in the warning colour of the theme (a rich text tag, so the repaint of the theme does not undo it).
        private static string BuildCaption()
        {
            string text = ModL10n.L("rtux.badge.new");
            try
            {
                GameColors colors = GameColors.Instance;
                if (colors != null) return "<color=#" + UnityColorUtility.ToHtmlStringRGB(colors.DynamicWarningColor()) + ">" + text + "</color>";
            }
            catch (Exception)
            {
                // No theme yet: the colour of the text is used.
            }
            return text;
        }
    }
}
