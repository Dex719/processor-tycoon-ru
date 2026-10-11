using System;
using System.Collections;
using PTMod.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The few helpers the page of the feature needs besides <c>ModUI</c>: a horizontal row for the tabs, emptying a container, and running an action one frame later (a
    /// widget must not be destroyed inside its own click handler). Only Unity's UI and the public part of <c>ModUI</c> are used. Main thread only.
    /// </summary>
    internal static class FlawUi
    {
        private const string Site = "ui/deferred";

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

        /// <summary>A horizontal row (full width, children side by side) in a container with a layout; <paramref name="height"/> above 0 fixes its height.</summary>
        internal static RectTransform Row(Transform parent, float height, float spacing)
        {
            var row = new GameObject("PTModDfrRow", typeof(RectTransform));
            try
            {
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
                int layer = parent.gameObject.layer;
                foreach (Transform node in row.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = layer;
                return (RectTransform)row.transform;
            }
            catch (Exception)
            {
                UnityEngine.Object.DestroyImmediate(row);
                throw;
            }
        }

        /// <summary>Runs the action one frame later under the guard of the feature (at once, guarded, when the host of the mod is not there).</summary>
        internal static void Defer(Action action)
        {
            if (action == null) return;

            PTModHost host = PTModHost.Instance;
            if (host != null && host.Run(OneFrameLater(action)) != null) return;
            ModHooks.Guard(DesignFlawRecallsFeature.FeatureId, Site, action);
        }

        private static IEnumerator OneFrameLater(Action action)
        {
            yield return null;
            ModHooks.Guard(DesignFlawRecallsFeature.FeatureId, Site, action);
        }
    }
}
