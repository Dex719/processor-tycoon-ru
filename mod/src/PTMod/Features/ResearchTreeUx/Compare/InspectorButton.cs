using System;
using System.Reflection;
using HarmonyLib;
using PTMod.Core;
using TMPro;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameInspectorWindow = ProcessorTycoon.Desktop.Inspector.InspectorWindow;
using UiLayoutGroup = UnityEngine.UI.LayoutGroup;
using UnityObject = UnityEngine.Object;
using UnityRectTransform = UnityEngine.RectTransform;
using UnityVector2 = UnityEngine.Vector2;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The button "Compare" in the window of the inspector, beside the button "Detailed / Simplified" (FR-6 item 1; the owner's decision: this
    /// window and the hub only, not the hardware window and not the card of the sales). A click opens the compare window with the processor the
    /// inspector shows now as CPU A (item 2).
    /// <list type="bullet">
    /// <item>The button is a button of <c>ModUI</c> (a clone of the game's button) put into the parent of <c>simpleOrDetailedButton</c> (a private
    /// field read with <c>AccessTools</c>, checked by the self-check). In a parent with a layout it joins the layout right after that button; in
    /// a parent without one it is given the anchors and the size of the neighbour and shifted sideways by its width, to the side that has room.</item>
    /// <item>The current processor is the private field <c>currentCpu</c>; a null one (the company has no processors) opens the window with A empty.</item>
    /// <item>Made once per window: a second <see cref="Attach"/> on the same window (the postfix of <c>Start</c> and the switching on of the feature
    /// both come) finds the button it made and does nothing. <see cref="Destroy"/> removes it (the feature is switched off, FR-8 item 3).</item>
    /// </list>
    /// Main thread only. Every entry point is called under a guard of the feature.
    /// </summary>
    internal sealed class InspectorButton
    {
        private const string ButtonName = "PTMod_RTUX_CompareButton";
        private const float Gap = 6f;
        private const float FallbackWidth = 100f;

        private static FieldInfo _simpleField;
        private static FieldInfo _cpuField;
        private static bool _fieldsResolved;

        private readonly ResearchTreeUxFeature _feature;
        private readonly GameInspectorWindow _window;
        private readonly ButtonUI _button;
        private bool _destroyed;

        private InspectorButton(ResearchTreeUxFeature feature, GameInspectorWindow window, ButtonUI button)
        {
            _feature = feature;
            _window = window;
            _button = button;
        }

        /// <summary>The window of the inspector the button stands in.</summary>
        public GameInspectorWindow Window
        {
            get { return _window; }
        }

        /// <summary>The button is still in the scene.</summary>
        public bool IsAlive
        {
            get { return !_destroyed && _button != null && _window != null; }
        }

        /// <summary>
        /// Puts the button into the inspector window. Null when the fields of the window are not there, the templates of the game are not looked up
        /// or the button could not be made (the log says which); the inspector is then as the game made it.
        /// </summary>
        public static InspectorButton Attach(ResearchTreeUxFeature feature, GameInspectorWindow window)
        {
            if (feature == null || window == null) return null;

            ResolveFields();
            if (_simpleField == null || _cpuField == null)
            {
                ModLog.Warn(ResearchTreeUxFeature.FeatureId, "compare: the inspector has no simpleOrDetailedButton or currentCpu, the button is not added");
                return null;
            }
            if (!ModUI.IsReady)
            {
                ModLog.Warn(ResearchTreeUxFeature.FeatureId, "compare: the interface templates of the game are not looked up yet, the button is not added");
                return null;
            }

            var simple = _simpleField.GetValue(window) as ButtonUI;
            if (simple == null || simple.transform.parent == null)
            {
                ModLog.Warn(ResearchTreeUxFeature.FeatureId, "compare: the button 'Detailed / Simplified' of the inspector is not there, the button is not added");
                return null;
            }

            UnityEngine.Transform parent = simple.transform.parent;
            UnityEngine.Transform old = parent.Find(ButtonName);
            if (old != null) UnityObject.Destroy(old.gameObject);   // a button of an earlier life of the feature that was not taken away

            InspectorButton result = null;
            ButtonUI made = ModUI.Button(parent, ModL10n.L("rtux.compare.button"), () => result.OnClick());
            if (made == null) return null;   // ModUI has logged why

            made.gameObject.name = ButtonName;
            result = new InspectorButton(feature, window, made);
            Place(simple, made);
            ModLog.Debug(ResearchTreeUxFeature.FeatureId, "compare: the button is in the inspector");
            return result;
        }

        /// <summary>Writes the caption again from the string tables (the language changed).</summary>
        public void RefreshCaption()
        {
            if (!IsAlive) return;

            TMP_Text label = _button.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = ModL10n.L("rtux.compare.button");
        }

        /// <summary>Takes the button out of the inspector (the feature is switched off or the scene is left). Safe to call twice.</summary>
        public void Destroy()
        {
            if (_destroyed) return;
            _destroyed = true;
            if (_button != null) UnityObject.Destroy(_button.gameObject);
        }

        // ---- the click ----

        private void OnClick()
        {
            if (!IsAlive) return;

            GameCpu current = null;
            try
            {
                current = _cpuField.GetValue(_window) as GameCpu;
            }
            catch (Exception e)
            {
                ModLog.Debug(ResearchTreeUxFeature.FeatureId, "compare: the processor of the inspector cannot be read (" + e.GetType().Name + ")");
            }
            _feature.OpenCompare(current, null);
        }

        // ---- the place ----

        // Beside the neighbour: in a layout right after it, otherwise with its anchors and size, shifted by its width to the side that has room.
        private static void Place(ButtonUI simple, ButtonUI made)
        {
            made.transform.SetSiblingIndex(simple.transform.GetSiblingIndex() + 1);

            var parent = simple.transform.parent as UnityRectTransform;
            var neighbour = simple.transform as UnityRectTransform;
            var rect = made.transform as UnityRectTransform;
            if (parent == null || neighbour == null || rect == null) return;
            if (parent.GetComponent<UiLayoutGroup>() != null) return;   // the layout of the parent puts it next to the neighbour

            rect.anchorMin = neighbour.anchorMin;
            rect.anchorMax = neighbour.anchorMax;
            rect.pivot = neighbour.pivot;
            rect.sizeDelta = neighbour.sizeDelta;

            float width = neighbour.rect.width > 1f ? neighbour.rect.width : FallbackWidth;
            float shift = width + Gap;
            rect.anchoredPosition = neighbour.anchoredPosition + new UnityVector2(NeighbourIsOnTheRight(parent, neighbour) ? -shift : shift, 0f);
        }

        // The neighbour lies in the right half of its parent: there is no room beyond it, the new button goes to its left.
        private static bool NeighbourIsOnTheRight(UnityRectTransform parent, UnityRectTransform neighbour)
        {
            UnityEngine.Vector3 centre = parent.InverseTransformPoint(neighbour.TransformPoint(neighbour.rect.center));
            return centre.x > parent.rect.center.x;
        }

        // ---- the fields of the game ----

        private static void ResolveFields()
        {
            if (_fieldsResolved) return;
            _fieldsResolved = true;

            _simpleField = AccessTools.Field(typeof(GameInspectorWindow), "simpleOrDetailedButton");
            _cpuField = AccessTools.Field(typeof(GameInspectorWindow), "currentCpu");
        }
    }
}
