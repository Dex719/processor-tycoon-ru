using System;
using PTMod.Core;
using TMPro;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using UiCanvasGroup = UnityEngine.CanvasGroup;
using UiHorizontalLayoutGroup = UnityEngine.UI.HorizontalLayoutGroup;
using UiImage = UnityEngine.UI.Image;
using UiLayoutElement = UnityEngine.UI.LayoutElement;
using UnityColor = UnityEngine.Color;
using UnityGameObject = UnityEngine.GameObject;
using UnityObject = UnityEngine.Object;
using UnityRectOffset = UnityEngine.RectOffset;
using UnityRectTransform = UnityEngine.RectTransform;
using UnityTextAnchor = UnityEngine.TextAnchor;
using UnityTransform = UnityEngine.Transform;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>The colours of the rows of the list, taken from the theme of the game (the same ones the buttons of the tree use).</summary>
    internal struct ListPalette
    {
        public UnityColor Panel;
        public UnityColor Normal;
        public UnityColor Current;
        public UnityColor Unnecessary;

        /// <summary>The warning colour as a rich text tag value ("#RRGGBB"), for the marks "new" and "not needed".</summary>
        public string WarningHex;

        /// <summary>Bumped on every change of the theme: a row repaints itself when its stamp differs.</summary>
        public int Stamp;
    }

    /// <summary>
    /// One row of the research list (FR-2 item 2): a technology on a line - the name (with the marks "new" and "not needed"), the year, the
    /// status, the time left, the cost per month and the button "Research". Built once from the widgets of <c>ModUI</c> the first time its
    /// technology comes into the list and reused after that: <see cref="Apply"/> writes only what changed since the last time, so a refresh of
    /// the whole list costs a few comparisons per row (NFR-1). The row follows the colours of the tree: the colour of the button of the tree
    /// for the state it stands for, dimmed for a technology that is done or locked.
    /// </summary>
    internal sealed class ResearchListRow
    {
        private const string RootName = "PTModListRow";
        private const float RowHeight = 36f;
        private const float YearWidth = 52f;
        private const float StatusWidth = 140f;
        private const float TimeWidth = 110f;
        private const float CostWidth = 120f;
        private const float ButtonWidth = 120f;
        private const float NameMinWidth = 150f;
        private const float DimmedResearched = 0.7f;
        private const float DimmedLocked = 0.6f;

        private readonly int _technologyId;
        private readonly UnityGameObject _root;
        private readonly UiImage _background;
        private readonly UiCanvasGroup _group;
        private readonly TMP_Text _name;
        private readonly TMP_Text _year;
        private readonly TMP_Text _status;
        private readonly TMP_Text _time;
        private readonly TMP_Text _cost;
        private readonly ButtonUI _button;

        private string _lastName;
        private string _lastYear;
        private string _lastStatus;
        private string _lastTime;
        private string _lastCost;
        private RowStatus _lastRowStatus = (RowStatus)(-1);
        private bool _lastMarked;
        private int _lastStamp = -1;
        private int _lastAction = -1;

        private ResearchListRow(int technologyId, UnityGameObject root, UiImage background, UiCanvasGroup group, TMP_Text name, TMP_Text year,
            TMP_Text status, TMP_Text time, TMP_Text cost, ButtonUI button)
        {
            _technologyId = technologyId;
            _root = root;
            _background = background;
            _group = group;
            _name = name;
            _year = year;
            _status = status;
            _time = time;
            _cost = cost;
            _button = button;
        }

        /// <summary><c>Technology.ID</c> of the technology on this row.</summary>
        public int TechnologyId
        {
            get { return _technologyId; }
        }

        /// <summary>The object of the row (the tooltip and the timer of "seen" go on it).</summary>
        public UnityGameObject Root
        {
            get { return _root; }
        }

        /// <summary>
        /// Makes the row of a technology under <paramref name="parent"/> (a vertical flow). Null when a widget of the game is missing in this
        /// scene or Unity refused an object; the half-made row is destroyed. <paramref name="onChoose"/> is called with the id on a click on
        /// "Research", under the guard of the feature.
        /// </summary>
        public static ResearchListRow Create(UnityTransform parent, int technologyId, string researchCaption, Action<int> onChoose)
        {
            UnityGameObject root = null;
            try
            {
                root = new UnityGameObject(RootName, typeof(UnityRectTransform));
                root.layer = parent.gameObject.layer;
                root.SetActive(false);   // nothing wakes up half-built
                var rect = (UnityRectTransform)root.transform;
                rect.SetParent(parent, false);

                // The background takes the pointer: the tooltip and the timer of "seen" listen on the row, and a click on its gaps goes nowhere.
                UiImage background = root.AddComponent<UiImage>();
                background.raycastTarget = true;
                UiCanvasGroup group = root.AddComponent<UiCanvasGroup>();

                UiHorizontalLayoutGroup layout = root.AddComponent<UiHorizontalLayoutGroup>();
                layout.spacing = 8f;
                layout.padding = new UnityRectOffset(8, 8, 2, 2);
                layout.childAlignment = UnityTextAnchor.MiddleLeft;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;

                UiLayoutElement size = root.AddComponent<UiLayoutElement>();
                size.minHeight = RowHeight;
                size.preferredHeight = RowHeight;
                size.flexibleWidth = 1f;

                TMP_Text name = Column(rect, NameMinWidth, 1f, TextAlignmentOptions.MidlineLeft, true);
                TMP_Text year = Column(rect, YearWidth, 0f, TextAlignmentOptions.MidlineLeft, false);
                TMP_Text status = Column(rect, StatusWidth, 0f, TextAlignmentOptions.MidlineLeft, false);
                TMP_Text time = Column(rect, TimeWidth, 0f, TextAlignmentOptions.MidlineRight, false);
                TMP_Text cost = Column(rect, CostWidth, 0f, TextAlignmentOptions.MidlineRight, false);
                if (name == null || year == null || status == null || time == null || cost == null)
                {
                    UnityObject.Destroy(root);
                    return null;
                }

                int id = technologyId;
                ButtonUI button = ModUI.Button(rect, researchCaption, () =>
                {
                    if (onChoose != null) onChoose(id);
                });
                if (button == null)
                {
                    UnityObject.Destroy(root);
                    return null;
                }

                UiLayoutElement buttonSize = button.GetComponent<UiLayoutElement>();
                if (buttonSize == null) buttonSize = button.gameObject.AddComponent<UiLayoutElement>();
                buttonSize.flexibleWidth = 0f;
                buttonSize.minWidth = ButtonWidth;
                buttonSize.preferredWidth = ButtonWidth;

                return new ResearchListRow(technologyId, root, background, group, name, year, status, time, cost, button);
            }
            catch (Exception e)
            {
                ModLog.Error(ResearchTreeUxFeature.FeatureId, "list: a row cannot be built", e);
                if (root != null) UnityObject.Destroy(root);
                return null;
            }
        }

        /// <summary>Puts the row in or out of the list.</summary>
        public void SetActive(bool active)
        {
            if (_root != null && _root.activeSelf != active) _root.SetActive(active);
        }

        /// <summary>
        /// Writes the technology into the row. Only what differs from the last call is written. <paramref name="isNew"/> adds the mark "new"
        /// to the name; the mark "not needed" is the text's own.
        /// </summary>
        public void Apply(TechSnapshot tech, ListRowText text, bool isNew, string newCaption, ListPalette palette)
        {
            if (_root == null || tech == null || text == null) return;

            RowStatus status = ListRowRules.StatusOf(tech);
            bool marked = text.Mark.Length > 0;

            string name = NameWithMarks(text, isNew, newCaption, palette.WarningHex);
            Write(_name, ref _lastName, name);
            Write(_year, ref _lastYear, text.Year);
            Write(_status, ref _lastStatus, text.Status);
            Write(_time, ref _lastTime, text.Time);
            Write(_cost, ref _lastCost, text.Cost);

            if (status != _lastRowStatus || marked != _lastMarked || palette.Stamp != _lastStamp)
            {
                _lastRowStatus = status;
                _lastMarked = marked;
                _lastStamp = palette.Stamp;
                _background.color = status == RowStatus.Current ? palette.Current : marked ? palette.Unnecessary : palette.Normal;
                _group.alpha = status == RowStatus.Researched ? DimmedResearched : status == RowStatus.Locked ? DimmedLocked : 1f;
            }

            ChooseAction action = ListRowRules.ActionOf(tech);
            if ((int)action != _lastAction)
            {
                _lastAction = (int)action;
                _button.Interactable = action != ChooseAction.None;
            }
        }

        /// <summary>Writes the caption of the button again (the language changed).</summary>
        public void SetButtonCaption(string caption)
        {
            if (_button != null) _button.Text = caption;
        }

        /// <summary>Removes the row.</summary>
        public void Destroy()
        {
            if (_root != null) UnityObject.Destroy(_root);
        }

        // ---- building ----

        private static TMP_Text Column(UnityRectTransform row, float width, float flexible, TextAlignmentOptions alignment, bool ellipsis)
        {
            TMP_Text text = ModUI.Text(row, string.Empty);
            if (text == null) return null;

            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = ellipsis ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
            text.richText = true;
            text.raycastTarget = false;

            UiLayoutElement layout = text.gameObject.GetComponent<UiLayoutElement>();
            if (layout == null) layout = text.gameObject.AddComponent<UiLayoutElement>();
            layout.minWidth = flexible > 0f ? width : 0f;
            layout.preferredWidth = width;
            layout.flexibleWidth = flexible;
            return text;
        }

        // ---- writing ----

        private static void Write(TMP_Text target, ref string last, string value)
        {
            if (string.Equals(last, value, StringComparison.Ordinal)) return;
            last = value;
            target.text = value;
        }

        // The name, then the mark "new" and the mark "not needed" in the warning colour of the theme (a rich text tag, so a repaint of the
        // theme by the core does not undo it).
        private static string NameWithMarks(ListRowText text, bool isNew, string newCaption, string warningHex)
        {
            bool marked = text.Mark.Length > 0;
            if (!isNew && !marked) return text.Name;

            string tail = string.Empty;
            if (isNew && !string.IsNullOrEmpty(newCaption)) tail += "  " + Colored(newCaption, warningHex);
            if (marked) tail += "  " + Colored("(" + text.Mark + ")", warningHex);
            return text.Name + tail;
        }

        private static string Colored(string text, string hex)
        {
            return string.IsNullOrEmpty(hex) ? text : "<color=" + hex + ">" + text + "</color>";
        }
    }
}
