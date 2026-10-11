using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using PTMod.Core;
using UnityEngine.EventSystems;
using GameSideWindow = ProcessorTycoon.Desktop.SideWindow.SideWindow;
using UiImage = UnityEngine.UI.Image;
using UiLayoutElement = UnityEngine.UI.LayoutElement;
using UiLayoutRebuilder = UnityEngine.UI.LayoutRebuilder;
using UnityColor = UnityEngine.Color;
using UnityGameObject = UnityEngine.GameObject;
using UnityObject = UnityEngine.Object;
using UnityRectTransform = UnityEngine.RectTransform;
using UnityRectTransformUtility = UnityEngine.RectTransformUtility;
using UnityVector2 = UnityEngine.Vector2;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The width of the side panel with the projects and the processors (FR-10, design: Component 11, decision D12): a strip on the left edge of
    /// <c>SideWindow</c> that the player drags to make the open panel wider, up to the factor of <c>ui.json</c> (1.6 times the original). The panel is
    /// not rebuilt and no method of the game is patched: the panel is the <c>RectTransform</c> the game moves between two positions
    /// (<c>visiblePosition</c> and <c>hiddenPosition</c>, private fields read and written with <c>AccessTools</c>), so a wider panel is the same
    /// rectangle with a larger size and the two positions moved by <see cref="SidebarWidthRule"/>: the open panel keeps its right edge on the screen
    /// edge, the shut panel hides exactly as far as before (AC-10.1). Every later move of the game (a project added, the switch pressed) goes to the new
    /// positions by itself.
    /// <list type="bullet">
    /// <item>The factor is kept in <c>ModProfile</c> (<c>sidebarWidthFactor</c>) and applied again when the scene is built (FR-10 item 2). A factor of 1 leaves the
    /// panel exactly as the game made it. A double click on the strip, or the button on the page of the feature in the hub, returns the original width.</item>
    /// <item><see cref="Detach"/> gives back the size and both positions the game had (FR-10 item 3); the panel is then as if the feature had never run.</item>
    /// <item>The spike of TSK-021 is a check at run time: after the size is set the panel is asked for the width it really has. A panel whose width is held by
    /// something else (a layout group above it) shows another width; the part then switches itself off and the panel is given back as it was (FR-10 item 4).</item>
    /// <item>A drag is accepted only while the panel is open. The strip straddles the left edge, mostly outside it (a few units each way), and is the last child of the
    /// panel, so it works whatever the panel is made of (a background child cannot cover it) and takes the pointer only in that narrow band. A press on it is passed on
    /// to the panel, which brings itself to the front as it does for every press.</item>
    /// </list>
    /// Main thread only. Every entry point is called under a guard of the feature (the drag events catch their own exceptions).
    /// </summary>
    internal sealed class SidebarResizer
    {
        private const string HandleName = "PTMod_RTUX_SidebarHandle";
        private const float HandleOutside = 5f;   // the part of the strip to the left of the edge of the panel, in the units of the panel
        private const float HandleInside = 3f;    // and the part over the panel itself
        private const float WidthTolerance = 1.5f;
        private const float MinPanelWidth = 1f;
        private const float MinScale = 1e-4f;
        private const float SameFactor = 1e-4f;

        private static FieldInfo _rectField;
        private static FieldInfo _visibleField;
        private static FieldInfo _hiddenField;
        private static FieldInfo _openField;
        private static bool _fieldsResolved;

        private readonly ResearchTreeUxFeature _feature;
        private readonly GameSideWindow _window;
        private readonly UnityRectTransform _rect;
        private readonly UnityVector2 _originalSizeDelta;
        private readonly float _originalVisible;
        private readonly float _originalHidden;
        private readonly float _originalWidth;   // in the units of the parent: the units the positions are in
        private readonly float _pivot;
        private readonly float _scale;           // the scale of the panel, to turn the units of the parent into the units of its size

        private float _visible;
        private float _hidden;
        private float _factor = SidebarWidthRule.NeutralFactor;
        private bool _dragging;
        private float _dragStartX;
        private float _dragStartFactor;
        private SidebarHandle _handle;
        private bool _destroyed;

        private SidebarResizer(ResearchTreeUxFeature feature, GameSideWindow window, UnityRectTransform rect, float visible, float hidden, float width)
        {
            _feature = feature;
            _window = window;
            _rect = rect;
            _originalSizeDelta = rect.sizeDelta;
            _originalVisible = visible;
            _originalHidden = hidden;
            _visible = visible;
            _hidden = hidden;
            _pivot = rect.pivot.x;
            _scale = Math.Abs(rect.localScale.x) < MinScale ? 1f : Math.Abs(rect.localScale.x);
            _originalWidth = width * _scale;
        }

        /// <summary>The panel of the game this resizer works on.</summary>
        public GameSideWindow Window
        {
            get { return _window; }
        }

        /// <summary>The panel and the strip are still in the scene.</summary>
        public bool IsAlive
        {
            get { return !_destroyed && _window != null && _rect != null; }
        }

        /// <summary>The factor the panel has now (1 is the original width).</summary>
        public float Factor
        {
            get { return _factor; }
        }

        /// <summary>
        /// Puts the strip on the panel and applies the factor of the profile. Null when the fields of the panel are not there, the panel has no size yet
        /// or the strip could not be made (the log says which); the panel is then as the game made it.
        /// </summary>
        public static SidebarResizer Attach(ResearchTreeUxFeature feature, GameSideWindow window)
        {
            if (feature == null || window == null) return null;

            ResolveFields();
            if (_rectField == null || _visibleField == null || _hiddenField == null)
            {
                ModLog.Warn(ResearchTreeUxFeature.FeatureId, "sidebar: the side panel has no rectTransform, visiblePosition or hiddenPosition, its width is not changed");
                return null;
            }

            UnityRectTransform rect = null;
            try
            {
                rect = _rectField.GetValue(window) as UnityRectTransform;
            }
            catch (Exception e)
            {
                ModLog.Debug(ResearchTreeUxFeature.FeatureId, "sidebar: rectTransform cannot be read (" + e.GetType().Name + ")");
            }
            if (rect == null) rect = window.transform as UnityRectTransform;   // what the game's own Awake does
            if (rect == null)
            {
                ModLog.Warn(ResearchTreeUxFeature.FeatureId, "sidebar: the side panel is not a rect transform, its width is not changed");
                return null;
            }

            float visible;
            float hidden;
            if (!TryRead(_visibleField, window, out visible) || !TryRead(_hiddenField, window, out hidden))
            {
                ModLog.Warn(ResearchTreeUxFeature.FeatureId, "sidebar: the positions of the side panel cannot be read, its width is not changed");
                return null;
            }

            float width = rect.rect.width;
            if (width < MinPanelWidth)
            {
                UiLayoutRebuilder.ForceRebuildLayoutImmediate(rect);   // the layout of the scene has not run yet
                width = rect.rect.width;
            }
            if (width < MinPanelWidth)
            {
                ModLog.Warn(ResearchTreeUxFeature.FeatureId, "sidebar: the side panel has no width yet, it is left as it is");
                return null;
            }

            var resizer = new SidebarResizer(feature, window, rect, visible, hidden, width);
            if (!resizer.CreateHandle())
            {
                resizer._destroyed = true;
                return null;
            }

            resizer.ApplySaved();
            ModLog.Debug(ResearchTreeUxFeature.FeatureId, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "sidebar: attached (width {0:0.#}, pivot {1:0.##}, visible {2:0.#}, hidden {3:0.#}, factor {4:0.###})",
                resizer._originalWidth, resizer._pivot, visible, hidden, resizer._factor));
            return resizer;
        }

        /// <summary>
        /// Gives the panel back as the game made it (the size and both positions) and takes the strip away (the feature is switched off, or the scene is
        /// left). Safe to call twice and after the panel is gone.
        /// </summary>
        public void Detach()
        {
            if (_destroyed) return;
            _destroyed = true;
            _dragging = false;

            SidebarHandle handle = _handle;
            _handle = null;
            if (handle != null) UnityObject.Destroy(handle.gameObject);

            if (_window == null || _rect == null) return;   // the scene is gone, nothing to give back
            if (SidebarWidthRule.IsNeutral(_factor)) return;   // the panel was never changed (or is back at the original): nothing to give back

            bool open = IsOpen();
            _rect.sizeDelta = _originalSizeDelta;
            _visible = _originalVisible;
            _hidden = _originalHidden;
            WritePositions();
            PlaceNow(open);
        }

        /// <summary>Returns the original width and remembers it (a double click on the strip, the button in the hub).</summary>
        public void ResetWidth()
        {
            if (!IsAlive) return;

            ApplyFactor(SidebarWidthRule.NeutralFactor);
            SaveFactor();
        }

        // ---- the drag (called by the strip) ----

        internal void BeginDrag(PointerEventData eventData)
        {
            _dragging = false;
            if (!IsAlive || eventData == null || eventData.button != PointerEventData.InputButton.Left) return;
            if (!IsOpen()) return;   // a shut panel is not dragged: only its edge shows

            float x;
            if (!TryParentX(eventData, out x)) return;

            _dragStartX = x;
            _dragStartFactor = _factor;
            _dragging = true;
            CancelTween();
        }

        internal void Drag(PointerEventData eventData)
        {
            if (!_dragging || !IsAlive || eventData == null) return;

            float x;
            if (!TryParentX(eventData, out x)) return;

            SidebarConfig config = _feature.SidebarConfig;
            float factor = SidebarWidthRule.FactorAfterDrag(_dragStartFactor, _originalWidth, _dragStartX - x, config.WidthMin, config.WidthMax);
            if (Math.Abs(factor - _factor) < SameFactor) return;

            ApplyFactor(factor);
        }

        internal void EndDrag(PointerEventData eventData)
        {
            if (!_dragging) return;
            _dragging = false;
            if (!IsAlive) return;

            SaveFactor();
            VerifyWidth();
        }

        /// <summary>A press on the strip: a double click returns the original width; the press itself goes on to the panel (it brings itself to the front).</summary>
        internal void Pressed(PointerEventData eventData)
        {
            if (!IsAlive || eventData == null) return;

            if (eventData.clickCount >= 2 && eventData.button == PointerEventData.InputButton.Left)
            {
                ResetWidth();
                return;
            }

            UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(_rect.gameObject, eventData, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
        }

        internal bool IsDragging
        {
            get { return _dragging; }
        }

        // ---- the width ----

        private void ApplySaved()
        {
            float saved = _feature.SidebarConfig.Clamp(ResearchUxPrefs.Current.SidebarWidthFactor);
            if (SidebarWidthRule.IsNeutral(saved)) return;   // the panel stays as the game made it

            ApplyFactor(saved);
            UxWidgets.Defer(VerifyWidth);   // one frame later: a layout above the panel has run by then
        }

        // The size, the two positions of the game and the place of the panel now. The factor is held inside the range of ui.json first.
        private void ApplyFactor(float requested)
        {
            float factor = _feature.SidebarConfig.Clamp(requested);
            bool open = IsOpen();   // before the positions change: it may be read from where the panel stands

            if (SidebarWidthRule.IsNeutral(factor))
            {
                _factor = SidebarWidthRule.NeutralFactor;
                _rect.sizeDelta = _originalSizeDelta;
                _visible = _originalVisible;
                _hidden = _originalHidden;
            }
            else
            {
                _factor = factor;
                float width = SidebarWidthRule.Width(_originalWidth, factor);
                _rect.SetSizeWithCurrentAnchors(UnityRectTransform.Axis.Horizontal, width / _scale);
                _visible = SidebarWidthRule.VisiblePosition(_originalVisible, _originalWidth, factor, _pivot);
                _hidden = SidebarWidthRule.HiddenPosition(_originalHidden, _originalWidth, factor, _pivot);
            }

            WritePositions();
            PlaceNow(open);
        }

        // The game moves the panel between these two fields (UpdateWindowVisibility), so writing them is all that later moves need.
        private void WritePositions()
        {
            _visibleField.SetValue(_window, _visible);
            _hiddenField.SetValue(_window, _hidden);
        }

        // Where the panel stands right now: at once, with the game's own move to the old place stopped (a move that runs would write the old place on).
        private void PlaceNow(bool open)
        {
            CancelTween();
            UnityVector2 position = _rect.anchoredPosition;
            position.x = open ? _visible : _hidden;
            _rect.anchoredPosition = position;
        }

        private bool IsOpen()
        {
            if (_openField != null)
            {
                try
                {
                    return (bool)_openField.GetValue(_window);
                }
                catch (Exception)
                {
                    // Not the flag the mod knows: judged by the place of the panel below.
                }
            }
            return SidebarWidthRule.LooksOpen(_rect.anchoredPosition.x, _visible, _hidden);
        }

        // The spike of TSK-021 at run time: does the panel really have the width that was set? Something above it that sizes it would say otherwise.
        private void VerifyWidth()
        {
            if (!IsAlive || SidebarWidthRule.IsNeutral(_factor)) return;

            float expected = SidebarWidthRule.Width(_originalWidth, _factor);
            float actual = _rect.rect.width * _scale;
            if (Math.Abs(actual - expected) <= WidthTolerance) return;

            _feature.OnSidebarRejected(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "the side panel did not take the width {0:0.#} (it is {1:0.#}): something above it sets its size", expected, actual));
        }

        private void SaveFactor()
        {
            UxPrefsModel prefs = ResearchUxPrefs.Current;
            if (Math.Abs(prefs.SidebarWidthFactor - _factor) < SameFactor) return;

            prefs.SidebarWidthFactor = _factor;
            ResearchUxPrefs.Save();
            ModLog.Debug(ResearchTreeUxFeature.FeatureId, string.Format(System.Globalization.CultureInfo.InvariantCulture, "sidebar: width factor {0:0.###} saved", _factor));
        }

        // The pointer in the units of the parent of the panel: the frame that does not change while the panel does.
        private bool TryParentX(PointerEventData eventData, out float x)
        {
            x = 0f;
            var parent = _rect.parent as UnityRectTransform;
            if (parent == null) return false;

            UnityVector2 local;
            if (!UnityRectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, eventData.pressEventCamera, out local)) return false;

            x = local.x;
            return !float.IsNaN(x) && !float.IsInfinity(x);
        }

        // ---- the strip ----

        private bool CreateHandle()
        {
            UnityEngine.Transform old = _rect.Find(HandleName);
            if (old != null) UnityObject.Destroy(old.gameObject);   // a strip of an earlier life of the feature that was not taken away

            var holder = new UnityGameObject(HandleName, typeof(UnityRectTransform));
            try
            {
                holder.layer = _rect.gameObject.layer;
                var strip = (UnityRectTransform)holder.transform;
                strip.SetParent(_rect, false);
                strip.anchorMin = new UnityVector2(0f, 0f);
                strip.anchorMax = new UnityVector2(0f, 1f);
                strip.pivot = new UnityVector2(HandleOutside / (HandleOutside + HandleInside), 0.5f);   // the pivot stands on the edge itself
                strip.anchoredPosition = UnityVector2.zero;
                strip.sizeDelta = new UnityVector2(HandleOutside + HandleInside, 0f);

                UiLayoutElement layout = holder.AddComponent<UiLayoutElement>();
                layout.ignoreLayout = true;   // a layout group on the panel must not take the strip into its rows

                UiImage image = holder.AddComponent<UiImage>();
                image.color = SidebarHandle.IdleColor;
                image.raycastTarget = true;

                SidebarHandle handle = holder.AddComponent<SidebarHandle>();
                handle.Setup(this, image);
                strip.SetAsLastSibling();   // above everything the panel is made of: a full-size background child could not cover it
                _handle = handle;

                AttachTooltip(holder);
                return true;
            }
            catch (Exception e)
            {
                ModLog.Error(ResearchTreeUxFeature.FeatureId, "sidebar: the strip for the width could not be made", e);
                UnityObject.DestroyImmediate(holder);
                return false;
            }
        }

        private static void AttachTooltip(UnityGameObject target)
        {
            try
            {
                ModUI.Tooltip(target, () => new ModUI.TooltipText(string.Empty, ModL10n.L("rtux.sidebar.handle.tip")));
            }
            catch (Exception e)
            {
                ModLog.Debug(ResearchTreeUxFeature.FeatureId, "sidebar: no tooltip on the strip (" + e.GetType().Name + ")");
            }
        }

        // ---- the game's move of the panel ----

        private void CancelTween()
        {
            try
            {
                CancelLean(_rect);
            }
            catch (Exception)
            {
                // No LeanTween of this shape in the game: nothing to stop, the move of the game ends by itself.
            }
        }

        // The game's type is touched only here: when it does not match, the JIT fails in this method and CancelTween catches it.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CancelLean(UnityRectTransform rect)
        {
            LeanTween.cancel(rect);
        }

        // ---- the fields of the game ----

        private static void ResolveFields()
        {
            if (_fieldsResolved) return;
            _fieldsResolved = true;

            _rectField = AccessTools.Field(typeof(GameSideWindow), "rectTransform");
            _visibleField = AccessTools.Field(typeof(GameSideWindow), "visiblePosition");
            _hiddenField = AccessTools.Field(typeof(GameSideWindow), "hiddenPosition");
            _openField = AccessTools.Field(typeof(GameSideWindow), "isOpen");
            if (_openField != null && _openField.FieldType != typeof(bool)) _openField = null;
        }

        private static bool TryRead(FieldInfo field, GameSideWindow window, out float value)
        {
            value = 0f;
            try
            {
                object boxed = field.GetValue(window);
                if (!(boxed is float)) return false;

                value = (float)boxed;
                return !float.IsNaN(value) && !float.IsInfinity(value);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// The strip on the left edge of the side panel (see <see cref="SidebarResizer"/>): it hands the drag, the press and the pointer to its owner and lights up
    /// under the pointer. An exception of the owner is counted against the feature here, at the border of the event, and does not reach Unity.
    /// </summary>
    internal sealed class SidebarHandle : UnityEngine.MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private const string Site = "sidebar/event";

        /// <summary>The colour of the strip at rest: a faint light band that tells the player the edge can be taken.</summary>
        public static readonly UnityColor IdleColor = new UnityColor(1f, 1f, 1f, 0.07f);

        /// <summary>The colour under the pointer and during the drag.</summary>
        public static readonly UnityColor ActiveColor = new UnityColor(1f, 1f, 1f, 0.32f);

        private SidebarResizer _owner;
        private UiImage _image;
        private bool _hover;

        public void Setup(SidebarResizer owner, UiImage image)
        {
            _owner = owner;
            _image = image;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            Run(() => _owner.BeginDrag(eventData));
            Paint();
        }

        public void OnDrag(PointerEventData eventData)
        {
            Run(() => _owner.Drag(eventData));
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            Run(() => _owner.EndDrag(eventData));
            Paint();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            Run(() => _owner.Pressed(eventData));
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hover = true;
            Paint();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hover = false;
            Paint();
        }

        private void OnDisable()
        {
            _hover = false;
            Paint();
        }

        private void Paint()
        {
            if (_image == null) return;
            bool dragging = _owner != null && _owner.IsDragging;
            _image.color = _hover || dragging ? ActiveColor : IdleColor;
        }

        private void Run(Action action)
        {
            if (_owner == null) return;

            try
            {
                action();
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(ResearchTreeUxFeature.FeatureId, Site, e);
            }
        }
    }
}
