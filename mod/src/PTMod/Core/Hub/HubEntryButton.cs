using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;

namespace PTMod.Core
{
    /// <summary>
    /// The "Mods" button that opens the hub (FR-17 items 1 and 7, owner's decision: a clone of <c>DoorButton</c>).
    /// <list type="bullet">
    /// <item><b>Where.</b> On <c>ModUI.Ready</c> of the game scene the <c>Button.Taskbar</c> template (<c>DoorButton</c> of the bottom bar) is cloned and put right of the original. The door button
    /// has an icon and no text, the mod has no icons of its own: the icon is hidden and a TextMeshPro caption (<c>core.hub.button</c>, the game's font, the theme's colour) is added, and the
    /// button grows to fit it. In a layout group the clone becomes the next sibling and the group places it; otherwise it takes the place next to the original (or to its left when
    /// the right side would leave the screen).</item>
    /// <item><b>Fallback.</b> If the taskbar template is missing, or cannot be cloned, the hub opens from a clone of <c>GameplaySettingsButton</c> in the pause menu (<c>Button.MenuFallback</c>)
    /// with the same caption, and a warning goes to the log; <see cref="UsesFallback"/> tells the Diagnostics tab to show it (<c>core.diag.buttonFallback</c>). With neither template there is no
    /// button and <see cref="IsInstalled"/> stays false; <see cref="ModHub.Open"/> still works.</item>
    /// <item><b>Click.</b> <see cref="ModHub.Open"/>, under the guard of the framework. The caption follows the language.</item>
    /// <item><b>Check in the game</b> (owner): the button reads "Mods"/"Моды", sits right of the door button without covering the speed buttons or the date, is in the colours of the theme (switch the
    /// theme: the caption changes colour), a click opens the hub and the time stops. <c>Player.log</c> with verbose log: <c>hub: the button 'Button.Taskbar' is placed ...</c>. If the button is in the wrong
    /// place or its frame is missing, send the line: the placement rules above are the assumptions to adjust.</item>
    /// </list>
    /// Main thread only. Nothing here throws into the game.
    /// </summary>
    internal static class HubEntryButton
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string CloneName = "PTModHubButton";
        private const float Gap = 8f;
        private const float TextPadding = 18f;

        private static GameObject _button;
        private static TMP_Text _label;
        private static bool _fallback;

        /// <summary>A hub button exists in the current scene.</summary>
        internal static bool IsInstalled
        {
            get { return _button != null; }
        }

        /// <summary>The button is the pause-menu fallback because the taskbar template was missing (the Diagnostics tab warns about it).</summary>
        internal static bool UsesFallback
        {
            get { return _fallback && _button != null; }
        }

        /// <summary>Makes the button for the current scene (the game scene only); a button made earlier in the scene is replaced.</summary>
        internal static void Install()
        {
            try
            {
                if (_button != null) UnityEngine.Object.Destroy(_button);
                Forget();
                if (PTModHost.CurrentScene != PTModHost.GameScene) return;

                if (TryInstall(ModUITemplates.ButtonTaskbar)) return;

                ModLog.Warn(CoreId, "hub: no taskbar button for the hub (" + (ModUITemplates.Has(ModUITemplates.ButtonTaskbar) ? "the clone failed" : "template " + ModUITemplates.ButtonTaskbar + " not found")
                                    + "); trying the pause menu");
                if (TryInstall(ModUITemplates.ButtonMenuFallback))
                {
                    _fallback = true;
                    return;
                }

                ModLog.Error(CoreId, "hub: neither the taskbar nor the pause menu has a template for the hub button; the hub can only be opened from code (ModHub.Open)");
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "hub: cannot make the hub button", ex);
            }
        }

        /// <summary>The scene is left: its objects go with it.</summary>
        internal static void Forget()
        {
            _button = null;
            _label = null;
            _fallback = false;
        }

        /// <summary>The language changed: the caption is written again.</summary>
        internal static void Relabel()
        {
            if (_label != null) _label.text = ModL10n.L("core.hub.button");
        }

        // ---- making the button ----

        private static bool TryInstall(string key)
        {
            GameObject template = ModUITemplates.Get(key);
            if (template == null) return false;

            GameObject clone = ModUITemplates.CloneWidget(key, CloneName);
            if (clone == null) return false;

            try
            {
                ButtonUI ui = clone.GetComponent<ButtonUI>();
                if (ui == null) throw new InvalidOperationException("the template " + key + " has no ButtonUI");

                TMP_Text label = LabelOf(clone, template);
                label.text = ModL10n.L("core.hub.button");
                FitWidth(clone, template, label);

                WidgetKit.Wake(clone);   // runs Awake of the game's scripts: ButtonUI finds the caption among the children here
                ui.OnClick += WidgetKit.Guarded(OnClick, "HubEntryButton/click");
                Place(clone, template, key);

                _button = clone;
                _label = label;
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Object.DestroyImmediate(clone);
                ModLog.Error(CoreId, "hub: cannot make the hub button from " + key, ex);
                return false;
            }
        }

        private static void OnClick()
        {
            ModHub.Open();
        }

        // The caption the template already has (the pause-menu button), or a new one over the button instead of the icon (the door button has no text).
        private static TMP_Text LabelOf(GameObject clone, GameObject template)
        {
            TMP_Text existing = clone.GetComponentInChildren<TMP_Text>(true);
            if (existing != null) return existing;

            HideIcon(clone, template);

            TextMeshProUGUI label = WidgetKit.CreateText(string.Empty, false, TextAlignmentOptions.Center);
            label.name = "PTModHubButtonLabel";
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;   // the button's own graphic takes the click

            WidgetKit.AdoptLayer(label.gameObject, clone.transform);
            label.transform.SetParent(clone.transform, false);
            var rect = (RectTransform)label.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return label;
        }

        // Hides the icon: the one OSButton names as its icon, or, when it cannot be told, every graphic below the root. A root graphic stays as the click target, blank.
        private static void HideIcon(GameObject clone, GameObject template)
        {
            Graphic icon = IconOf(template, clone);
            if (icon != null)
            {
                Blank(icon, clone);
                return;
            }

            foreach (Graphic graphic in clone.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic is TMP_Text || graphic.gameObject == clone) continue;
                Blank(graphic, clone);
            }
        }

        private static void Blank(Graphic graphic, GameObject root)
        {
            if (graphic.gameObject != root)
            {
                graphic.enabled = false;
                return;
            }

            graphic.color = new Color(0f, 0f, 0f, 0f);   // still a raycast target: the button stays clickable
            var image = graphic as Image;
            if (image != null) image.sprite = null;
        }

        // The graphic <c>OSButton.osIcon</c> of the template, found again in the clone by its place in the hierarchy; null when it cannot be told.
        private static Graphic IconOf(GameObject template, GameObject clone)
        {
            try
            {
                foreach (Component component in template.GetComponents<Component>())
                {
                    if (component == null || !string.Equals(component.GetType().Name, "OSButton", StringComparison.Ordinal)) continue;

                    FieldInfo field = component.GetType().GetField("osIcon", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    Graphic source = field == null ? null : field.GetValue(component) as Graphic;
                    if (source == null) return null;

                    Transform match = Mirror(template.transform, source.transform, clone.transform);
                    return match == null ? null : match.GetComponent<Graphic>();
                }
            }
            catch (Exception ex)
            {
                ModLog.Debug(CoreId, "hub: the icon of the button template cannot be found (" + ex.GetType().Name + "); every graphic below the root is hidden");
            }
            return null;
        }

        // The node of the copy that sits where <paramref name="node"/> sits in the original: the same path of child indices.
        private static Transform Mirror(Transform originalRoot, Transform node, Transform copyRoot)
        {
            var path = new List<int>();
            for (Transform step = node; step != originalRoot; step = step.parent)
            {
                if (step == null) return null;
                path.Add(step.GetSiblingIndex());
            }

            Transform found = copyRoot;
            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (path[i] >= found.childCount) return null;
                found = found.GetChild(path[i]);
            }
            return found;
        }

        // ---- size and place ----

        // The button is at least as wide as the original and as the caption with padding; a group that lays the button out is told the width too.
        private static void FitWidth(GameObject clone, GameObject template, TMP_Text label)
        {
            var rect = clone.transform as RectTransform;
            var source = template.transform as RectTransform;
            if (rect == null || source == null) return;

            float wanted = Mathf.Max(source.rect.width, label.preferredWidth + 2f * TextPadding);
            if (Mathf.Approximately(rect.anchorMin.x, rect.anchorMax.x)) rect.sizeDelta = new Vector2(wanted, rect.sizeDelta.y);

            Transform parent = template.transform.parent;
            if (parent != null && parent.GetComponent<LayoutGroup>() != null)
            {
                LayoutElement layout = clone.GetComponent<LayoutElement>();
                if (layout == null) layout = clone.AddComponent<LayoutElement>();
                layout.minWidth = wanted;
                layout.preferredWidth = wanted;
            }
        }

        // Next to the original: the next sibling in a layout group, otherwise the same anchors shifted by one button width.
        private static void Place(GameObject clone, GameObject template, string key)
        {
            Transform parent = template.transform.parent;
            CloneSanitizer.Attach(clone, parent, true);

            if (parent.GetComponent<LayoutGroup>() != null)
            {
                clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
                ModLog.Debug(CoreId, "hub: the button '" + key + "' is placed as the next sibling of the original in a layout group");
                return;
            }

            var rect = (RectTransform)clone.transform;
            var source = (RectTransform)template.transform;
            if (!Mathf.Approximately(rect.anchorMin.x, rect.anchorMax.x))
            {
                clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
                ModLog.Debug(CoreId, "hub: the button '" + key + "' is stretched in its parent; it keeps the place of the original (no room to shift it)");
                return;
            }

            Vector2 at = source.anchoredPosition;
            float right = source.rect.width * (1f - source.pivot.x) + Gap + rect.sizeDelta.x * rect.pivot.x;
            rect.anchoredPosition = at + new Vector2(right, 0f);
            string side = "right";
            if (!IsOnScreen(rect))
            {
                float left = source.rect.width * source.pivot.x + Gap + rect.sizeDelta.x * (1f - rect.pivot.x);
                rect.anchoredPosition = at - new Vector2(left, 0f);
                side = "left";
            }
            clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
            ModLog.Debug(CoreId, "hub: the button '" + key + "' is placed " + side + " of the original at " + rect.anchoredPosition + ", size " + rect.sizeDelta);
        }

        private static bool IsOnScreen(RectTransform rect)
        {
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            RectTransform root = canvas == null ? null : canvas.rootCanvas.transform as RectTransform;
            if (root == null) return true;

            var corners = new Vector3[4];
            var bounds = new Vector3[4];
            rect.GetWorldCorners(corners);   // 0 bottom-left, 2 top-right
            root.GetWorldCorners(bounds);
            const float slack = 0.5f;
            return corners[0].x >= bounds[0].x - slack && corners[2].x <= bounds[2].x + slack
                   && corners[0].y >= bounds[0].y - slack && corners[2].y <= bounds[2].y + slack;
        }
    }
}
